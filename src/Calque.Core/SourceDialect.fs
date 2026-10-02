module internal Calque.Core.SourceDialect

open Calque.Core.SyntaxOak

// Inspect syntax roles in the Oak, keeping strings, comments and quoted identifiers intact.
// Clef has no CLR widening, nulls or object model. This guard refuses those parser forms;
// it does not resolve names or decide what a qualified function call means.
let ensureSupported (oak: Oak) : unit =
  let unsupported text =
    raise (FormatException($"Calque cannot yet format the Clef source form '{text}'."))

  let refuse text =
    raise (FormatException($"'{text}' is not permitted in Clef source."))

  let inspectTypeIdentifier (identifier: IdentListNode) =
    let names =
      identifier.Content
      |> List.choose (function IdentifierOrDot.Ident token -> Some token.Text | _ -> None)

    match names with
    | [ "obj" ] -> refuse "obj type"
    | [ "System"; "Object" ]
    | [ "global"; "System"; "Object" ] -> refuse "System.Object type"
    | _ -> ()

  // Long identifiers serve several source roles. Only a Type.LongIdent is an explicit type use;
  // a binding called obj, a field called obj or a qualified call must stay ordinary syntax.
  let rec inspectType t =
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
    match node with
    | :? ITypeDefn as declaration when not declaration.Members.IsEmpty -> refuse "type members"
    | _ -> ()

    match node with
    | :? SingleTextNode as token ->
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
    | :? ExprSingleNode as expression when expression.Leading.Text = "upcast" || expression.Leading.Text = "downcast" ->
      refuse expression.Leading.Text
    | :? ExprPrefixAppNode as expression when expression.Operator.Text = "%" || expression.Operator.Text = "%%" ->
      refuse "quotation splice"
    | :? PatIsInstNode -> refuse "type-test pattern"
    | :? ExprObjExprNode -> refuse "object expression"
    | :? ExprNewNode -> refuse "object constructor"
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
    | :? BindingNode as binding -> inspectMemberKeywords binding.LeadingKeyword
    | :? PatParameterNode as parameter -> parameter.Type |> Option.iter inspectType
    | :? BindingReturnInfoNode as annotation -> inspectType annotation.Type
    | :? FieldNode as field -> inspectType field.Type
    | :? TypeSpreadNode as spread -> inspectType spread.Type
    | :? TypeDefnAbbrevNode as abbreviation -> inspectType abbreviation.Type
    | :? ExprTypeAppNode as application -> application.TypeParameters |> List.iter inspectType
    | :? ValNode as value ->
      value.LeadingKeyword |> Option.iter inspectMemberKeywords
      inspectType value.Type
    | :? OpenTargetNode as target -> inspectType target.Target
    | :? ExternBindingNode as binding -> inspectType binding.Type
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
