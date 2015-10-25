import Prelude

Signature "Monad" 'M {
    TypeFunc "MCons" => *

    TypeFunc 'M 'a => ">>=" => ('a => 'M 'b) => 'M 'b
    TypeFunc "return" 'a => 'M 'a
  }
