namespace rec Fable.AST.Go

/// Go type representations
type GoType =
    /// Primitive types: int, int8, int16, int32, int64, uint, uint8, uint16, uint32, uint64, uintptr
    /// float32, float64, string, bool, byte, rune, error
    | GoPrimitive of string
    /// Pointer type: *T
    | GoPointer of GoType
    /// Slice type: []T
    | GoSlice of GoType
    /// Fixed-size array type: [N]T
    | GoArray of size: int * GoType
    /// Map type: map[K]V
    | GoMap of key: GoType * value: GoType
    /// Channel type: chan T or <-chan T or chan<- T
    | GoChannel of direction: GoChannelDir * GoType
    /// Function type: func(A, B) (R, error) etc
    | GoFunc of params: GoType list * returns: GoType list
    /// Named/interface type reference
    | GoNamed of package: string option * name: string
    /// Struct type (inline definition)
    | GoStruct of fields: GoStructField list
    /// Interface type (inline definition)
    | GoInterface of methods: GoInterfaceMethod list
    /// Type parameter (for generics)
    | GoTypeParam of name: string
    /// Empty interface (any)
    | GoEmpty

and GoChannelDir =
    | BiDir
    | Recv  // <-chan
    | Send  // chan<-

and GoStructField =
    {
        Name: string
        Type: GoType
        Tag: string option  // struct tag like `json:"field"`
    }

and GoInterfaceMethod =
    {
        Name: string
        Signature: GoType  // Should be GoFunc
    }

/// Go constants/literals
type GoConst =
    | GoConstInt of int64
    | GoConstFloat of float
    | GoConstString of string
    | GoConstBool of bool
    | GoConstRune of char
    | GoConstNil

/// Go expressions
and GoExpr =
    /// Identifier: x, foo, Foo, etc
    | GoIdent of string
    /// Literal constant
    | GoConst of GoConst
    /// Binary operator: +, -, *, /, %, ==, !=, <, >, <=, >=, &&, ||, &, |, ^, <<, >>, &^
    | GoBinaryOp of op: string * left: GoExpr * right: GoExpr
    /// Unary operator: -, +, !, ^, *, &, <-
    | GoUnaryOp of op: string * operand: GoExpr
    /// Function call: f(a, b, c)
    | GoCall of func: GoExpr * args: GoExpr list
    /// Method call: x.M(a, b)
    | GoMethodCall of receiver: GoExpr * method: string * args: GoExpr list
    /// Index expression: a[i]
    | GoIndex of array: GoExpr * index: GoExpr
    /// Slice expression: a[lo:hi], a[lo:hi:cap]
    | GoSliceExpr of array: GoExpr * low: GoExpr option * high: GoExpr option * cap: GoExpr option
    /// Field/selector access: x.Field
    | GoSelector of receiver: GoExpr * field: string
    /// Composite literal: T{field: value, ...} or []T{a, b, c} or map[K]V{k: v}
    | GoCompositeLit of typ: GoType * fields: GoFieldValue list
    /// Type assertion: x.(T)
    | GoTypeAssertion of expr: GoExpr * typ: GoType
    /// Function literal (closure): func(a, b) { ... }
    | GoFuncLit of params: GoParam list * returns: GoType list * body: GoStmt list
    /// Address-of operator: &x
    | GoAddr of expr: GoExpr
    /// Dereference operator: *p
    | GoDeref of ptr: GoExpr
    /// Make expression: make([]T, len), make(chan T, buf), make(map[K]V)
    | GoMake of typ: GoType * args: GoExpr list
    /// Type conversion: T(expr)
    | GoTypeConversion of typ: GoType * expr: GoExpr
    /// Spread/variadic: expr...
    | GoSpread of GoExpr

and GoFieldValue =
    {
        Key: string option  // None for slice elements, Some for struct/map fields
        Value: GoExpr
    }

and GoParam =
    {
        Name: string option  // None for unnamed params (used in type signatures)
        Type: GoType
    }

/// Go statements
and GoStmt =
    /// Short variable declaration: x := expr or x, y := a, b
    | GoShortDecl of names: string list * values: GoExpr list
    /// Variable declaration: var x T or var x T = expr
    | GoVarDecl of name: string * typ: GoType option * init: GoExpr option
    /// Const declaration: const x T = expr
    | GoConstDecl of name: string * typ: GoType option * value: GoExpr
    /// Simple assignment: x = expr or x, y = a, b
    | GoAssign of targets: GoExpr list * values: GoExpr list
    /// Add-assign, subtract-assign, etc: x += expr, x -= expr, etc
    | GoAugAssign of target: GoExpr * op: string * value: GoExpr
    /// Expression statement: f(); foo()
    | GoExprStmt of GoExpr
    /// Block of statements: { ... }
    | GoBlock of GoStmt list
    /// If statement: if guard { ... } else { ... }
    | GoIf of guard: GoExpr * thenBody: GoStmt list * elseBody: GoStmt list
    /// For loop: for init; cond; post { ... }
    | GoFor of init: GoStmt option * cond: GoExpr option * post: GoStmt option * body: GoStmt list
    /// Range-based for: for k, v := range x { ... } or for i := range x { ... }
    | GoRangeFor of key: string option * value: string option * expr: GoExpr * body: GoStmt list
    /// Switch statement: switch x { case 1: ... default: ... }
    | GoSwitch of tag: GoExpr option * cases: GoSwitchCase list
    /// Type switch: switch x := x.(type) { case int: ... default: ... }
    | GoTypeSwitch of name: string option * expr: GoExpr * cases: GoTypeSwitchCase list
    /// Return statement: return or return a, b
    | GoReturn of values: GoExpr list
    /// Break statement
    | GoBreak
    /// Continue statement
    | GoContinue
    /// Label definition followed by statement: label: stmt
    | GoLabeled of label: string * stmt: GoStmt
    /// Defer statement: defer f()
    | GoDefer of GoExpr
    /// Go statement: go f()
    | GoGo of GoExpr
    /// Select statement: select { case ...: ... default: ... }
    | GoSelect of cases: GoSelectCase list

and GoSwitchCase =
    {
        Values: GoExpr list  // Empty for default case
        Body: GoStmt list
    }

and GoTypeSwitchCase =
    {
        Type: GoType option  // None for default case
        Body: GoStmt list
    }

and GoSelectCase =
    {
        Comm: GoExpr option  // None for default case; typically a channel operation
        Body: GoStmt list
    }

/// Function parameter
and GoFuncParam =
    {
        Name: string
        Type: GoType
    }

/// Function declaration
and GoFunc =
    {
        Name: string
        TypeParams: string list  // Type parameter names for generics
        Params: GoFuncParam list
        Returns: GoType list
        Body: GoStmt list
    }

/// Method declaration (with receiver)
and GoMethod =
    {
        Receiver: string * GoType  // (name, type)
        Name: string
        TypeParams: string list
        Params: GoFuncParam list
        Returns: GoType list
        Body: GoStmt list
    }

/// Type declaration (struct, interface, type alias)
and GoTypeDecl =
    {
        Name: string
        TypeParams: string list
        Spec: GoTypeSpec
    }

and GoTypeSpec =
    | GoStructType of fields: GoStructField list
    | GoInterfaceType of methods: GoInterfaceMethod list
    | GoTypeAlias of GoType

/// Top-level declaration
and GoDecl =
    | GoDeclFunc of GoFunc
    | GoDeclMethod of GoMethod
    | GoDeclType of GoTypeDecl
    | GoDeclVar of name: string * typ: GoType option * init: GoExpr option
    | GoDeclConst of name: string * typ: GoType option * value: GoExpr
    | GoDeclBlock of GoStmt list  // For init() blocks or other statement blocks

/// Import specification
type GoImport =
    {
        Path: string
        Alias: string option  // import myalias "path/to/package"
    }

/// Go source file
type GoFile =
    {
        Filename: string
        Package: string  // package name (must be valid identifier)
        Imports: GoImport list
        Decls: (int * GoDecl) list  // (priority, declaration) for ordering
    }
