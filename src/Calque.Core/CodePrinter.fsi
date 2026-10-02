module internal Calque.Core.CodePrinter

open Calque.Core.Context
open Calque.Core.SyntaxOak

val genFile: oak: Oak -> (Context -> Context)
