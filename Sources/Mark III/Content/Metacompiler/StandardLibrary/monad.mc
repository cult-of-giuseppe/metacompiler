import prelude

TypeFunc "Monad" => (* => *) => Module

Monad 'M => Module {
    ArrowFunc 'M 'a -> ">>=" -> ('a -> 'M 'b) -> 'M 'b   #> 10 L
    Func "return" -> 'a -> 'M 'a

    Func "MCons" -> *
    MCons -> 'M

    Func "returnFrom" -> 'a -> 'a
    returnFrom a -> a

    Func "lift" -> ('a -> 'b ) -> 'M 'a -> 'M 'b
    a >>= a'
    --
    lift f a -> return(f a')

    Func "lift2" -> ('a -> 'b -> 'c) -> 'M 'a -> 'M 'b -> 'M 'c
    a >>= a'
    b >>= b'
    --
    lift2 f a b -> return(f a' b')

    $$ TypeFunc "liftM" => (M' 'a -> M' 'b) => M M' 'a => M M' 'b
    TypeFunc "liftM" => (* => *) => * => *
    N >>= a
    f a -> b
    lift^N(return^N b) >>= res
    --
    liftM f N -> return res
  }
