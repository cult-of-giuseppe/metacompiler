﻿module Codegen
open Common
open CodegenInterface
open Mangle

type NamespacedItem = Ns       of string*List<NamespacedItem>
                    | Data     of string*data
                    | Function of string*List<rule>
                    | Lambda   of int*rule

let construct_tree (input:fromTypecheckerWithLove) :List<NamespacedItem> =
  let rec datatree (s:List<NamespacedItem>) (idx:List<string>,v:string*data) :List<NamespacedItem> =
    match idx with
    | []    -> (Data(v))::s
    | n::ns -> match s |> List.partition (fun x->match x with Ns(n,_)->true | _->false) with
               | [Ns(n,body)],rest -> Ns(n,datatree body (ns,v))::rest
               | [],list           -> Ns(n,datatree []   (ns,v))::list
  let rec functree (s:List<NamespacedItem>) (idx:List<string>,v:string*List<rule>) :List<NamespacedItem> =
    match idx with
    | []    -> (Function(v))::s
    | n::ns -> match s |> List.partition (fun x->match x with Ns(n,_)->true | _->false) with
               | [Ns(n,body)],rest -> Ns(n,functree body (ns,v))::rest
               | [],list           -> Ns(n,functree []   (ns,v))::list
  let rec lambdatree (s:List<NamespacedItem>) (idx:List<string>,v:int*rule) :List<NamespacedItem> =
    match idx with
    | []    -> (Lambda(v))::s
    | n::ns -> match s |> List.partition (fun x->match x with Ns(n,_)->true | _->false) with
               | [Ns(n,body)],rest -> Ns(n,lambdatree body (ns,v))::rest
               | [],list           -> Ns(n,lambdatree []   (ns,v))::list
  let go input output state = input |> Seq.map (fun (n,d)->(List.rev n.Namespace),(n.Name,d)) |> Seq.fold output state
  [] |> go (Map.toSeq input.lambdas) lambdatree 
     |> go (Map.toSeq input.rules) functree 
     |> go input.datas datatree

let print_literal lit =
  match lit with
  | I64 i    -> sprintf "%d" i
  | U64 i    -> sprintf "%u" i
  | F64 i    -> sprintf "%f" i
  | String s -> sprintf "\"%s\"" s
  | Bool b   -> if b then "true" else "false"

let print_predicate (p:predicate) :string= 
  match p with Less -> "<" | LessEqual -> "<=" | Equal -> "=" | GreaterEqual -> ">=" | Greater -> ">" | NotEqual -> "!="

let field (n:int) (t:Type) :string =
  sprintf "public %s _arg%d;\n" (mangle_type t) n

let highest_tmp (typemap:Map<local_id,Type>): int =
  typemap |> Map.fold (fun s k _ -> match k with Tmp(x) when x>s -> x | _ -> s) 0

let rec premisse (m:Map<local_id,Type>) (ps:premisse list) (ret:local_id) =
  match ps with 
  | [] -> sprintf "_ret.Add(%s);\n" (mangle_local_id ret)
  | p::ps -> 
    match p with
    | Literal x -> sprintf "var %s = %s;\n%s"
                     (mangle_local_id x.dest)
                     (print_literal x.value)
                     (premisse m ps ret)
    | Conditional x -> sprintf "if(%s %s %s){%s}"
                         (mangle_local_id x.left)
                         (print_predicate x.predicate)
                         (mangle_local_id x.right)
                         (premisse m ps ret)
    | Destructor x ->
      let new_id  = (Tmp(1+(highest_tmp m)))
      sprintf "var %s = %s as %s;\nif(%s!=null){\n%s%s}\n"
        (mangle_local_id new_id)
        (mangle_local_id x.source)
        (mangle_id       x.destructor)
        (mangle_local_id new_id)
        (x.args|>List.mapi(fun nr arg->sprintf "var %s=%s._arg%d;\n" (mangle_local_id arg) (mangle_local_id new_id) nr)|>String.concat "")
        (premisse (m|>Map.add new_id (McType(x.destructor))) ps ret)
    | McClosure x -> sprintf "var %s = new %s();\n%s" 
                       (mangle_local_id x.dest)
                       (mangle_rule_id x.func)
                       (premisse m ps ret)
    | DotNetClosure x -> sprintf "var %s = new _dotnet.%s();\n%s" 
                           (mangle_local_id x.dest)
                           (mangle_id x.func)
                           (premisse m ps ret)
    | ConstructorClosure x -> sprintf "var %s = new %s();\n%s" 
                                (mangle_local_id x.dest)
                                (mangle_id x.func)
                                (premisse m ps ret)
    | Application x -> sprintf "var %s = %s; %s.%s=%s;\n%s"
                         (mangle_local_id  x.dest)
                         (mangle_local_id  x.closure)
                         (mangle_local_id  x.dest)
                         (sprintf "_arg%d" x.argnr)
                         (mangle_local_id  x.argument)
                         (premisse m ps ret)
    | ImpureApplicationCall x
    | ApplicationCall x -> sprintf "%s.%s=%s;\nforeach(var %s in %s._run()){\n%s}\n"
                             (mangle_local_id  x.closure)
                             (sprintf "_arg%d" x.argnr)
                             (mangle_local_id  x.argument)
                             (mangle_local_id  x.dest)
                             (mangle_local_id  x.closure)
                             (premisse m ps ret)

let print_rule (rule:rule) = 
  sprintf "{\n%s%s}\n"
    (rule.input|>List.mapi (fun i x->sprintf "var %s=_arg%d;\n" (mangle_local_id x) i) |> String.concat "")
    (premisse rule.typemap rule.premis rule.output)

let print_rule_bodies (rules:rule list) =
  rules |> List.map print_rule |> String.concat ""

let print_main (rule:rule) =
  let return_type = mangle_type rule.typemap.[rule.output]
  let body = sprintf "static System.Collections.Generic.List<%s> body(){var _ret = new System.Collections.Generic.List<%s>();\n%sreturn _ret;\n}" return_type return_type (print_rule_bodies [rule])
  let main = "static void Main() {\nforeach(var res in body()){System.Console.WriteLine(System.String.Format(\"{0}\", res));\n}\n}"
  sprintf "class _main{\n%s%s}\n" body main 

let rec print_tree (lookup:fromTypecheckerWithLove) (ns:List<NamespacedItem>) :string =
  let build_func (name:string) (rules:rule list) = 
    let rule = List.head rules
    let args = rule.input |> Seq.mapi (fun nr id-> sprintf "public %s _arg%d;\n" (mangle_type rule.typemap.[id]) nr) |> String.concat ""
    let ret_type = mangle_type rule.typemap.[rule.output]
    let rules = print_rule_bodies rules
    sprintf "class %s{\n%spublic System.Collections.Generic.List<%s> _run(){\nvar _ret = new System.Collections.Generic.List<%s>();\n%sreturn _ret;\n}\n}\n" (CSharpMangle name) args ret_type ret_type rules
  let print_base_types (ns:List<NamespacedItem>) = 
    let types = ns |> List.fold (fun types item -> match item with Data (_,v) -> v.outputType::types | _ -> types) [] |> List.distinct
    let print t = sprintf "public class %s{}\n" (t|>remove_namespace_of_type|>mangle_type)
    List.map print types
  let go ns =
    match ns with
    | Ns(n,ns)      -> sprintf "namespace %s{\n%s}\n" (CSharpMangle n) (print_tree lookup ns)
    | Data(n,d)     -> sprintf "public class %s:%s{\n%s}\n" (CSharpMangle n) (d.outputType|>remove_namespace_of_type|>mangle_type) (d.args|>List.mapi field|>String.concat "")
    | Function(name,rules) -> build_func name rules
    | Lambda(number,rule)  -> build_func (sprintf "_lambda%d" number) [rule]
  (print_base_types ns)@(ns|>List.map go)|>String.concat "\n"
    
let get_locals (ps:premisse list) :local_id list =
  ps |> List.collect (fun p ->
    match p with
    | Literal             x -> [x.dest]
    | Conditional         x -> [x.left;x.right]
    | Destructor          x -> x.source::x.args
    | McClosure           x -> [x.dest]
    | DotNetClosure       x -> [x.dest]
    | ConstructorClosure  x -> [x.dest]
    | Application         x
    | ImpureApplicationCall x 
    | ApplicationCall     x -> [x.closure;x.dest;x.argument] )

let foldi (f:'int->'state->'element->'state) (s:'state) (lst:seq<'element>) :'state =
  let fn ((counter:'int),(state:'state)) (element:'element) :'counter*'state = 
    counter+1,(f counter state element)
  let _,ret = lst|>Seq.fold fn (0,s)
  ret 

let validate (input:fromTypecheckerWithLove) :bool =
  let ice () = 
      do System.Console.BackgroundColor <- System.ConsoleColor.Red
      do System.Console.Write "INTERNAL COMPILER ERROR"
      do System.Console.ResetColor()
  let print_local_id (id:local_id) = match id with Named(x)->x | Tmp(x)->sprintf "temporary(%d)" x
  let print_id (id:Id) = String.concat "^" (id.Name::id.Namespace)
  let check_typemap (id:Id) (rule:rule) :bool =
    let expected = (get_locals rule.premis) @ rule.input |> List.distinct |> List.sort
    let received  = rule.typemap |> Map.toList |> List.map (fun (x,_)->x) |> List.sort
    if expected = received then true
    else 
      do ice()
      do printf " incorrect typemap in rule %s:\n  expected: %A\n  received: %A\n" (print_id id) expected received
      false
  let check_dest_constness (id:Id) (rule:rule) (success:bool):bool =
      let per_premisse (statementnr:int) (set:Set<local_id>,success:bool) (premisse:premisse) :Set<local_id>*bool =
        let check (set:Set<local_id>,success:bool) (local:local_id) =
          if set.Contains(local) then
            do ice()
            do printf " %s assigned twice in rule %s, statement %d\n" (print_local_id local) (print_id id) statementnr
            set,false
          else set.Add(local),success
        match premisse with
        | Literal x               -> check (set,success) x.dest
        | Conditional _           -> set,success
        | Destructor x            -> x.args |> Seq.fold check (set,success)
        | McClosure  x            -> check (set,success) x.dest
        | DotNetClosure x         -> check (set,success) x.dest
        | ConstructorClosure x    -> check (set,success) x.dest
        | Application x           -> check (set,success) x.dest
        | ApplicationCall x       -> check (set,success) x.dest
        | ImpureApplicationCall x -> check (set,success) x.dest
      let _,ret = rule.premis |> foldi per_premisse (Set.empty,success)
      ret
  (true,input.rules) ||> Map.fold (fun (success:bool) (id:Id) (rules:rule list)-> 
    if rules.IsEmpty then
      do ice()
      do printf " empty rule: %s\n" (print_id id)
      false
    else
      (true,input.main::rules) ||> List.fold (fun (success:bool) (rule:rule) -> if check_typemap id rule then (check_dest_constness id rule success) else false))

let failsafe_codegen(input:fromTypecheckerWithLove) :Option<string>=
  if validate input then
    let foo = input |> construct_tree |> print_tree input
    foo+(print_main input.main) |> Some
  else None
