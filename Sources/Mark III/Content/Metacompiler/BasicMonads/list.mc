import prelude
import match
import monad

TypeAlias "List" => * => *
List 'a => Unit | ('a * (List 'a))

Func 'a -> "::" -> List 'a -> List 'a
x :: xs -> Right(x,xs)

Func "empty" -> List 'a
empty -> Left Unit


Func List 'a -> "@" -> List 'a -> List 'a  #> 200
empty @ l -> l
(x :: xs) @ l -> x :: (xs @ l)


Func "map" -> List 'a -> ('a -> 'b) -> List 'b
map empty f -> empty
map (x :: xs) f -> (map (f x)) :: xs


Func "filter" -> List 'a -> ('a -> Boolean) -> List 'a
filter empty p -> empty

(if p x then 
  (x :: (filter xs p)) 
  else 
  (filter xs p)
) -> res
--
filter (x :: xs) p -> res


TypeAlias "ListT" => (* => *) => * => *
ListT 'M 'a => 'M(List 'a)

TypeFunc "list" => Monad => Monad 

list 'M => Monad(ListT MCons^'M) {
    lm >>=^'M l
    (do^(matchOr(MCons 'a)) l with
      (\empty -> return^'M empty)
      (\(x :: xs) -> 
        k x >>=^'M y
        ((return^'M xs) >>= k) >>=^'M ys
        return^'M (y @ ys)
      )
    ) => res
    --
    lm >>= k => res

    return x => return^'M(x :: empty)
  }
