module internal Calque.Core.SourceDialect

open Calque.Core.SyntaxOak
open System.Collections.Generic

// Inspect syntax roles in the Oak, keeping strings, comments and quoted identifiers intact.
// Refuse surface forms that the Clef contract excludes. This remains a syntax
// guard; type, lifetime and proof settlement belong to CCS.
let ensureSupportedWithCheckpoint checkpoint (oak: Oak) : unit =
  let unsupported text =
    raise (FormatException($"Calque cannot yet format the Clef source form '{text}'."))

  let refuse text =
    raise (FormatException($"'{text}' is not permitted in Clef source."))

  let namesOfIdentifier (identifier: IdentListNode) =
    identifier.Content
    |> List.choose (function IdentifierOrDot.Ident token -> Some token.Text | _ -> None)

  let withoutGlobal = function
    | "global" :: rest -> rest
    | names -> names

  let inspectReference names =
    match withoutGlobal names with
    | "NativePtr" :: _
    | "Microsoft" :: "FSharp" :: "NativeInterop" :: _ -> refuse "raw-pointer interop"
    | "Unchecked" :: _
    | "Microsoft" :: "FSharp" :: "Core" :: "Unchecked" :: _ -> refuse "Unchecked construction"
    | "Microsoft" :: "FSharp" :: "Core" :: "Operators" :: operation :: _
        when List.contains operation ["box"; "unbox"; "typeof"; "typedefof"] -> refuse "boxing or runtime type reification"
    | "System" :: ("IntPtr" | "UIntPtr") :: _ -> refuse "raw-pointer interop"
    | "System" :: ("Type" | "RuntimeTypeHandle") :: _ -> refuse "runtime type reification"
    | _ -> ()

  let inspectTypeIdentifier (identifier: IdentListNode) =
    let names = namesOfIdentifier identifier |> withoutGlobal
    match names with
    | [ "obj" ] -> refuse "obj type"
    | [ "System"; "Object" ] -> refuse "System.Object type"
    | [ "nativeptr" ]
    | [ "voidptr" ]
    | [ "void*" ]
    | [ "System"; "IntPtr" ]
    | [ "System"; "UIntPtr" ] -> refuse "raw-pointer type"
    | [ "System"; "Type" ]
    | [ "System"; "RuntimeTypeHandle" ] -> refuse "runtime type reification"
    | _ -> inspectReference names

  // The Oak keeps dotted names as chains. Read just their name prefix: a call
  // argument, index or computed receiver never becomes part of that name.
  let rec expressionName = function
    | Expr.Ident identifier -> Some [identifier.Text]
    | Expr.OptVar identifier -> Some (namesOfIdentifier identifier.Identifier)
    | Expr.TypeApp application -> expressionName application.Identifier
    | Expr.Paren expression -> expressionName expression.Expr
    | Expr.Chain chain ->
      chain.Segments
      |> List.fold (fun prefix segment ->
        match prefix, segment with
        | Some names, (ChainSegment.DotMember(_, memberExpr) | ChainSegment.DotApplication(_, memberExpr, _)) ->
          expressionName memberExpr |> Option.map (fun suffix -> names @ suffix)
        | Some names, _ -> Some names
        | _ -> None) (expressionName chain.Head)
    | _ -> None

  let inspectExpressionName expression =
    expressionName expression |> Option.iter (fun names ->
      inspectReference names
      match withoutGlobal names with
      | [ "box" ]
      | [ "unbox" ]
      | [ "typeof" ]
      | [ "typedefof" ] -> refuse "boxing or runtime type reification"
      | [ "stackalloc" ] -> refuse "raw-pointer allocation"
      | _ -> ())

  // Member-name fragments are expressions in the inherited Oak. Remember
  // their exact nodes so .box/.NativePtr do not become bare intrinsic names.
  let memberNames = HashSet<Node>()
  let rec markMemberName expression =
    checkpoint ()
    memberNames.Add(Expr.Node expression) |> ignore
    match expression with
    | Expr.TypeApp application -> markMemberName application.Identifier
    | Expr.Paren expression -> markMemberName expression.Expr
    | Expr.Chain chain ->
      markMemberName chain.Head
      chain.Segments |> List.iter (function
        | ChainSegment.DotMember(_, name) | ChainSegment.DotApplication(_, name, _) -> markMemberName name
        | _ -> ())
    | _ -> ()

  // Long identifiers serve several source roles. Only a Type.LongIdent is an explicit type use;
  // a binding called obj, a field called obj or a qualified call must stay ordinary syntax.
  let rec inspectType t =
    checkpoint ()
    let inspectPath path =
      path |> List.iter (function Choice1Of2 t -> inspectType t | Choice2Of2 _ -> ())

    match t with
    | Type.LongIdent identifier -> inspectTypeIdentifier identifier
    | Type.Funs node ->
      node.Parameters |> List.iter (fst >> inspectType)
      inspectType node.ReturnType
    | Type.Tuple node -> inspectPath node.Path
    | Type.HashConstraint _ -> refuse "flexible CLR subtype"
    | Type.MeasurePower node -> inspectType node.BaseMeasure
    | Type.StaticConstant _
    | Type.StaticConstantExpr _
    | Type.Anon _
    | Type.Var _ -> ()
    | Type.StaticConstantNamed node ->
      inspectType node.Identifier
      inspectType node.Value
    | Type.Array node -> inspectType node.Type
    | Type.AppPostfix node ->
      inspectType node.First
      inspectType node.Last
    | Type.AppPrefix node ->
      inspectType node.Identifier
      node.Arguments |> List.iter inspectType
    | Type.StructTuple node -> inspectPath node.Path
    | Type.WithSubTypeConstraint constraintNode -> inspectConstraint constraintNode
    | Type.WithGlobalConstraints node ->
      inspectType node.Type
      node.TypeConstraints |> List.iter inspectConstraint
    | Type.AnonRecord node -> node.Fields |> List.iter (snd >> inspectType)
    | Type.Paren node -> inspectType node.Type
    | Type.SignatureParameter node -> inspectType node.Type
    | Type.Or node ->
      inspectType node.LeftHandSide
      inspectType node.RightHandSide
    | Type.LongIdentApp node ->
      inspectType node.AppType
      inspectTypeIdentifier node.LongIdent
    | Type.Intersection node -> inspectPath node.TypesAndSeparators

  and inspectConstraint constraintNode =
    checkpoint ()
    match constraintNode with
    | TypeConstraint.Single _
    | TypeConstraint.WhereNotSupportsNull _ -> ()
    | TypeConstraint.DefaultsToType node -> inspectType node.Type
    | TypeConstraint.SubtypeOfType _ -> refuse "CLR subtype constraint"
    | TypeConstraint.SupportsMember _ -> unsupported "compile-time member constraint"
    | TypeConstraint.EnumOrDelegate node ->
      if node.Verb = "delegate" then refuse "delegate constraint"
      node.Types |> List.iter inspectType
    | TypeConstraint.WhereSelfConstrained t -> inspectType t

  let inspectMemberKeywords (keywords: MultipleTextsNode) =
    keywords.Content
    |> List.iter (fun token ->
      match token.Text with
      | "member"
      | "override"
      | "abstract"
      | "default"
      | "new" -> refuse token.Text
      | _ -> ())

  let rec visit (node: Node) =
    checkpoint ()
    match node with
    | :? ITypeDefn as declaration when not declaration.Members.IsEmpty -> refuse "type members"
    | _ -> ()

    match node with
    | :? SingleTextNode as token ->
      if token.IsExpressionIdentifier && not (memberNames.Contains node) then
        inspectExpressionName (Expr.Ident token)
      match token.Text with
      | "eager"
      | "__LINE__"
      | "__SOURCE_DIRECTORY__"
      | "__SOURCE_FILE__" -> unsupported token.Text
      | "null" -> refuse token.Text
      | _ -> ()
    | :? ParsedHashDirectiveNode as directive when directive.Ident = "line" -> unsupported "#line"
    | :? ExprTypedNode as expression ->
      match expression.Operator with
      | ":>"
      | ":>>"
      | ":?>"
      | ":?" -> refuse expression.Operator
      | _ -> inspectType expression.Type
    | :? ExprSingleNode as expression when
        expression.Leading.Text = "upcast" || expression.Leading.Text = "downcast" ||
        expression.Leading.Text = "fixed" || expression.Leading.Text = "&&" ->
      refuse expression.Leading.Text
    | :? ExprPrefixAppNode as expression when expression.Operator.Text = "%" || expression.Operator.Text = "%%" ->
      refuse "quotation splice"
    | :? PatIsInstNode -> refuse "type-test pattern"
    | :? ExprObjExprNode -> refuse "object expression"
    | :? ExprNewNode -> refuse "object constructor"
    // Only the builder position names .NET task; a binding or field called task stays ordinary syntax.
    | :? ExprNamedComputationNode as computation ->
      match expressionName computation.Name |> Option.map withoutGlobal with
      | Some [ "task" ]
      | Some [ "Microsoft"; "FSharp"; "Control"; "task" ]
      | Some [ "Microsoft"; "FSharp"; "Control"; "TaskBuilder"; "task" ] -> refuse "task computation expression"
      | _ -> ()
    | :? ExprChain as expression ->
      if not (memberNames.Contains node) then inspectExpressionName (Expr.Chain expression)
      expression.Segments |> List.iter (function
        | ChainSegment.DotMember(_, name) | ChainSegment.DotApplication(_, name, _) -> markMemberName name
        | _ -> ())
    | :? ExprOptVarNode as identifier ->
      if not (memberNames.Contains node) then inspectExpressionName (Expr.OptVar identifier)
    | :? ExprAppNode as application ->
      inspectExpressionName application.FunctionExpr
      application.Arguments |> List.iter inspectExpressionName
    | :? ExprAppSingleParenArgNode as application -> inspectExpressionName application.FunctionExpr
    | :? ExprAppWithLambdaNode as application -> inspectExpressionName application.FunctionName
    | :? ExprTraitCallNode -> unsupported "compile-time member invocation"
    | :? TypeDefnRegularNode -> refuse "class or interface declaration"
    | :? TypeDefnExplicitBodyNode as body when body.Kind.Text = "class" || body.Kind.Text = "interface" ->
      refuse body.Kind.Text
    | :? TypeDefnAugmentationNode -> refuse "type augmentation"
    | :? TypeDefnDelegateNode -> refuse "delegate declaration"
    | :? MemberDefnInterfaceNode -> refuse "interface implementation"
    | :? MemberDefnInheritNode
    | :? InheritConstructorTypeOnlyNode
    | :? InheritConstructorUnitNode
    | :? InheritConstructorParenNode
    | :? InheritConstructorOtherNode -> refuse "inherit"
    | :? MemberDefnExplicitCtorNode
    | :? MemberDefnAutoPropertyNode
    | :? MemberDefnAbstractSlotNode
    | :? MemberDefnPropertyGetSetNode
    | :? MemberDefnSigMemberNode -> refuse "member declaration"
    | :? BindingNode as binding ->
      inspectMemberKeywords binding.LeadingKeyword
      inspectExpressionName binding.Expr
    | :? PatParameterNode as parameter -> parameter.Type |> Option.iter inspectType
    | :? BindingReturnInfoNode as annotation -> inspectType annotation.Type
    | :? FieldNode as field -> inspectType field.Type
    | :? TypeSpreadNode as spread -> inspectType spread.Type
    | :? TypeDefnAbbrevNode as abbreviation -> inspectType abbreviation.Type
    | :? ExprTypeAppNode as application ->
      if not (memberNames.Contains node) then inspectExpressionName application.Identifier
      application.TypeParameters |> List.iter inspectType
    | :? ValNode as value ->
      value.LeadingKeyword |> Option.iter inspectMemberKeywords
      inspectType value.Type
    | :? OpenTargetNode as target -> inspectType target.Target
    | :? OpenModuleOrNamespaceNode as declaration -> inspectReference (namesOfIdentifier declaration.Name)
    | :? ModuleAbbrevNode as declaration -> inspectReference (namesOfIdentifier declaration.Alias)
    | :? AttributeNode as attribute ->
      match namesOfIdentifier attribute.TypeName |> withoutGlobal with
      | [ "DllImport" ]
      | [ "DllImportAttribute" ]
      | [ "System"; "Runtime"; "InteropServices"; "DllImport" ]
      | [ "System"; "Runtime"; "InteropServices"; "DllImportAttribute" ] -> refuse "managed P/Invoke"
      | _ -> ()
    | :? ExternBindingNode -> refuse "managed extern declaration"
    | :? ExternBindingPatternNode as parameter -> parameter.Type |> Option.iter inspectType
    | :? StaticOptimizationConstraintWhenTyparTyconEqualsTyconNode as constraintNode -> inspectType constraintNode.Type
    | :? TypeConstraintDefaultsToTypeNode as constraintNode -> inspectType constraintNode.Type
    | :? TypeConstraintSubtypeOfTypeNode -> refuse "CLR subtype constraint"
    | :? TypeConstraintSupportsMemberNode -> unsupported "compile-time member constraint"
    | :? TypeHashConstraintNode -> refuse "flexible CLR subtype"
    | :? TypeConstraintEnumOrDelegateNode as constraintNode ->
      if constraintNode.Verb = "delegate" then refuse "delegate constraint"
      constraintNode.Types |> List.iter inspectType
    | _ -> ()

    node.Children |> Array.iter visit

  visit oak

let ensureSupported oak = ensureSupportedWithCheckpoint ignore oak
