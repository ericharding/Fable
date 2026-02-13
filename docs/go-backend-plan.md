# Fable Go Backend — Implementation Plan

## Overview

Add Go as a compilation target for Fable, enabling F# code to be transpiled to idiomatic Go.
This follows the established pattern of existing backends (JS, Python, Dart, Rust, PHP).

The plan is organized into **phases** that can be tackled incrementally, with each phase
producing a working (if limited) compiler. The phases are ordered by dependency — later
phases build on earlier ones.

---

## Architecture Summary

The compilation pipeline for every Fable target language follows this flow:

```
F# Source → FSharp2Fable → Fable AST → FableTransforms → Fable2Go → Go AST → GoPrinter → .go files
```

We need to build three core components plus a runtime library:

| Component | File(s) | Role |
|-----------|---------|------|
| **Go AST** | `Go.fs` | F# types representing Go syntax nodes |
| **Fable2Go** | `Fable2Go.fs` | Transform Fable AST → Go AST |
| **GoPrinter** | `GoPrinter.fs` | Serialize Go AST → `.go` source text |
| **Runtime library** | `fable-library-go/` | Go package implementing F# stdlib types |

---

## Phase 0 — Scaffolding & Wiring

**Goal:** `fable --lang go` is accepted, routes through the pipeline, and emits an empty/stub `.go` file.

### 0.1 Add `Go` to the `Language` discriminated union
- File: `src/Fable.AST/Plugins.fs`
- Add `| Go` case and `ToString()` override

### 0.2 Add CLI parsing
- File: `src/Fable.Cli/Entry.fs`
- Add `"go" | "golang"` cases in `argLanguage`
- Update the error message's "Available options" list

### 0.3 Create backend directory & stub files
- Directory: `src/Fable.Transforms/Go/`
- Files:
  - `Go.fs` — minimal AST (empty `GoFile` type)
  - `Fable2Go.fs` — stub `transformFile` that returns an empty `GoFile`
  - `GoPrinter.fs` — stub `run` that writes `package main`

### 0.4 Wire into Pipeline.fs
- File: `src/Fable.Cli/Pipeline.fs`
- Add `module Go` with `GoWriter` and `compileFile`
- Add `| Go -> Go.compileFile ...` to the dispatch match

### 0.5 Add files to Fable.Transforms.fsproj
- The `.fsproj` file lists F# sources in compilation order; new files must be added

### 0.6 Default file extension
- Decide on `.go` extension for output files
- Wire up extension default in CLI args handling

**Deliverable:** `fable --lang go src/MyProject.fsproj -o out/` produces empty `.go` files that parse with `go build` (just `package` declarations).

---

## Phase 1 — Go AST Design

**Goal:** Define Go AST types that cover the language constructs we'll need.

Go is syntactically simpler than most Fable targets. The AST should model:

### 1.1 Core expression types (`GoExpr`)
- Identifiers, literals (int, float, string, bool, rune, nil)
- Binary/unary operators
- Function calls, method calls
- Index expressions (`a[i]`)
- Selector expressions (`a.B`)
- Slice expressions (`a[lo:hi]`)
- Composite literals (`T{fields...}`)
- Type assertions (`x.(T)`)
- Func literals (closures)
- Address-of (`&x`) and dereference (`*x`)

### 1.2 Core statement types (`GoStmt`)
- Variable declarations (`var`, `:=`, `const`)
- Assignment (single and multi-return)
- If/else
- For loops (C-style, range-based)
- Switch (expression switch and type switch)
- Return
- Go / Defer
- Block

### 1.3 Top-level declarations (`GoDecl`)
- Function declarations
- Method declarations (with receiver)
- Struct type definitions
- Interface type definitions
- Type aliases
- Package-level var/const

### 1.4 Type representations (`GoType`)
- Primitive types (int, float64, string, bool, rune, byte, etc.)
- Pointer types (`*T`)
- Slice types (`[]T`)
- Map types (`map[K]V`)
- Channel types (`chan T`)
- Func types (`func(A, B) R`)
- Struct types (inline)
- Interface types (inline)
- Named types (references)
- Type parameters (generics, Go 1.18+)

### 1.5 File-level structure (`GoFile`)
- Package name
- Imports
- Top-level declarations

### Design decisions
- **Follow the PHP pattern**: Keep the AST lean (~150-250 lines). We don't need to model
  every Go syntax construct — only what Fable will actually generate.
- **Use Go 1.18+ generics**: This avoids needing `interface{}` everywhere and produces
  more readable output.
- **Model imports explicitly**: Go has strict import rules (unused imports = compile error),
  so the AST needs to track imports that get pruned during printing.

---

## Phase 2 — Primitives & Basic Expressions

**Goal:** Compile F# programs that use primitive types, arithmetic, let bindings, and simple functions.

### 2.1 Fable2Go: Value/literal translation
Map `Fable.Value` cases to Go literals:
- `BoolConstant` → `true`/`false`
- `NumberConstant` → Go numeric literals
- `StringConstant` → Go string literals (handle escaping)
- `CharConstant` → Go rune literals
- `UnitConstant` → nothing (or `struct{}{}` where needed)
- `Null` → `nil`

### 2.2 Fable2Go: Binary/unary operations
Map `Fable.OperationKind` to Go operators. Most map directly.
Special cases:
- String concatenation: `+` works in Go too
- Equality: `==` for primitives, need custom for structural equality
- Bitwise ops: map directly
- Exponentiation: `math.Pow()` call

### 2.3 Fable2Go: Let bindings
`Fable.Let(ident, value, body)` → Go `var` or `:=` declarations.
- Mutable bindings → `var x T = ...`
- Immutable bindings → `x := ...` (or `var` with type annotation)

### 2.4 Fable2Go: Simple function declarations
`Fable.MemberDecl` for module-level functions → Go `func` declarations.
- Map F# parameter types to Go types
- Map return types
- Handle unit return (void in Go)

### 2.5 GoPrinter: Expression & statement printing
Implement the printer for all AST nodes defined in Phase 1, covering:
- Operator precedence and parenthesization
- String escaping
- Indentation

### 2.6 GoPrinter: Import management
- Track which imports are actually used
- Only emit used imports (Go compilation fails on unused imports)
- Handle `fable-library-go` import paths

**Deliverable:** Simple F# like this compiles and runs:
```fsharp
let add x y = x + y
let result = add 3 4
printfn "%d" result
```

---

## Phase 3 — Control Flow

**Goal:** If/else, pattern matching (simple cases), loops.

### 3.1 If/then/else
`Fable.IfThenElse` → Go `if/else`. Nested else-if chains should flatten.

### 3.2 Decision trees (pattern matching)
Fable compiles `match` expressions into `DecisionTree` + `DecisionTreeSuccess`.
These need to map to Go `switch` statements or `if/else` chains.
This is one of the most complex parts — study how the PHP backend handles
`Fable.DecisionTree` and `Fable.DecisionTreeSuccess`.

### 3.3 While loops
`Fable.WhileLoop` → Go `for` loop (Go uses `for` for all loops).

### 3.4 For loops
`Fable.ForLoop` → Go `for i := start; i < limit; i++`.

### 3.5 Try/catch/finally
`Fable.TryCatch` → Go doesn't have exceptions.
**Design decision needed:** Options include:
- Use `panic`/`recover` (closest to exceptions but not idiomatic)
- Transform to error returns (idiomatic but changes semantics)
- Use panic/recover for the initial implementation, document the tradeoff

**Deliverable:** Pattern matching, conditionals, and loops work.

---

## Phase 4 — Functions & Closures

**Goal:** Higher-order functions, closures, currying, piping.

### 4.1 Lambda expressions
`Fable.Lambda` → Go func literals (closures). Go closures capture by reference,
which aligns with F# mutable captures but differs for immutable bindings.

### 4.2 Currying
F# functions are curried by default. Go functions are not.
**Strategy:** Generate uncurried Go functions and wrap with currying helpers
where partial application is detected. The `Replacements.fs` module handles
identifying when currying is needed vs. when a direct call suffices.

### 4.3 Pipe operator
`|>` is already desugared by FCS into a function application, so this should
work automatically once function calls work.

### 4.4 Delegates / function values
When F# passes functions as values, these become Go `func(...)` values.
Need to handle both named functions-as-values and inline lambdas.

**Deliverable:** Code using `|>`, `List.map`, partial application works.

---

## Phase 5 — Records & Discriminated Unions

**Goal:** Core F# data types compile to Go structs and tagged unions.

### 5.1 Records → Go structs
`Fable.ClassDecl` for records → Go `struct` type definitions.
- Fields map to exported Go struct fields (PascalCase)
- Generate constructor function `NewMyRecord(...)`
- Handle `{ record with field = newValue }` (copy-with-update)
- Implement structural equality (`Equals` method or `==` for simple cases)

### 5.2 Discriminated Unions → Go interfaces + concrete types
**Strategy (interface-based):**
```go
type Shape interface { isShape() }
type Circle struct { Radius float64 }
func (Circle) isShape() {}
type Rectangle struct { Width, Height float64 }
func (Rectangle) isShape() {}
```
- Each union case becomes a concrete struct
- A marker interface ties them together
- Pattern matching uses type switches

**Alternative (tag-based):**
```go
type Shape struct {
    Tag  int
    Fields []interface{}
}
```
Simpler but loses type safety. Recommend starting with interface-based.

### 5.3 Anonymous records
`Fable.AnonymousRecordType` → Go struct literals or named generated types.

### 5.4 Equality and comparison
- Implement `Equals()` and `CompareTo()` methods on generated types
- Required for pattern matching and collection operations

**Deliverable:** Records, DUs, and pattern matching over them works end to end.

---

## Phase 6 — Collections & Replacements (Runtime Library)

**Goal:** Build `fable-library-go` with core collection types and wire up `Replacements.fs`.

### 6.1 Runtime library structure
Directory: `src/fable-library-go/`
```
fable_library/
  array.go       — Array/slice operations (map, filter, fold, etc.)
  list.go        — Immutable linked list
  map.go         — Immutable map (tree-based or wrapper)
  set.go         — Immutable set
  option.go      — Option[T] type
  result.go      — Result[T,E] type
  string.go      — String utilities
  numeric.go     — Numeric conversions and operations
  reflection.go  — Runtime type information
  async.go       — Async computation support
  seq.go         — Lazy sequences (IEnumerable equivalent)
  util.go        — Miscellaneous helpers
  go.mod         — Module definition
```

### 6.2 Replacements.fs
- File: `src/Fable.Transforms/Go/Replacements.fs`
- This is the largest file in every backend (~2000-4000 lines)
- Maps F# standard library calls to Go runtime library calls
- Priority order:
  1. String operations
  2. Array operations (map, filter, fold, sort, etc.)
  3. List operations
  4. Option/Result operations
  5. Math operations
  6. Seq operations
  7. Map/Set operations
  8. DateTime operations
  9. Regex operations

### 6.3 Import resolution
- Map `Fable.Import` to Go import paths
- Handle `fable-library-go` module imports
- Handle user module imports (relative paths → Go package paths)

**Deliverable:** Standard library functions like `List.map`, `Array.filter`,
`String.split`, `Option.map` etc. work.

---

## Phase 7 — Classes, Interfaces & Object Expressions

**Goal:** F# classes and interfaces compile to Go structs and interfaces.

### 7.1 Classes → Go structs with methods
- Class fields → struct fields
- Methods → methods with receiver
- Constructors → factory functions
- Properties → getter/setter methods
- Inheritance → embedded structs (composition)

### 7.2 Interfaces → Go interfaces
- F# interface members → Go interface method signatures
- Interface implementation → method sets on structs

### 7.3 Object expressions
`Fable.ObjectExpr` → anonymous struct implementing an interface,
or a named generated type.

### 7.4 Type casting & testing
- `Fable.TypeTest` → Go type assertions or type switches
- `:?>` operator → type assertion with panic on failure
- `:?` pattern → type assertion with ok check

**Deliverable:** OOP-style F# code compiles.

---

## Phase 8 — Async & Concurrency

**Goal:** F# async workflows and tasks map to Go concurrency primitives.

### 8.1 Strategy decision
Options:
- **Goroutines + channels:** Most idiomatic Go, but semantic mismatch with F# async
- **Promise-like wrapper type:** A `Task[T]` struct that wraps goroutine results
- **Hybrid:** Use goroutines internally, expose promise-like API

Recommend starting with a `Task[T]` wrapper in fable-library-go that uses
goroutines internally, since F# async is fundamentally about composable
asynchronous computations, not concurrent message passing.

### 8.2 Async/Task builders
- `Fable.Call` on async builder methods → Go runtime library calls
- `async { ... }` → goroutine launch wrapped in Task
- `let! x = ...` → channel receive or callback
- `Async.Parallel` → `sync.WaitGroup` or similar

### 8.3 Basic concurrency primitives
- Locks → `sync.Mutex`
- Atomic operations → `sync/atomic`

**Deliverable:** Simple async F# code runs using goroutines.

---

## Phase 9 — String Formatting & Console

**Goal:** `printf`-family functions and string interpolation.

### 9.1 printf/printfn/sprintf
F# format strings → Go `fmt.Printf`/`fmt.Sprintf` with format translation.
- `%d` → `%d` (same)
- `%s` → `%s` (same)
- `%f` → `%f` (same)
- `%A` → custom pretty-printer (needs reflection support)
- `%O` → `%v` (Go's default formatting)

### 9.2 String interpolation
`$"Hello {name}"` → `fmt.Sprintf("Hello %v", name)`

**Deliverable:** Console output works for basic programs.

---

## Phase 10 — Testing Infrastructure

**Goal:** Run the existing Fable test suite against the Go backend.

### 10.1 Test project setup
- Directory: `tests/Go/`
- `Fable.Tests.Go.fsproj` project file
- Test runner that compiles F# → Go, then runs `go test`

### 10.2 Test categories (incremental enablement)
Start with simple tests and progressively enable more:
1. Arithmetic / primitives
2. String operations
3. Arrays
4. Lists
5. Records
6. Unions / pattern matching
7. Classes / interfaces
8. Async
9. Reflection

### 10.3 CI integration
- Add Go tests to `.github/workflows/build.yml`
- Require Go toolchain in CI environment

**Deliverable:** Automated test suite validates Go backend correctness.

---

## Phase 11 — Refinement & Ergonomics

**Goal:** Make the output idiomatic and handle edge cases.

### 11.1 Code formatting
- Run `gofmt` or `goimports` on output (or generate already-formatted code)
- Go has strict formatting standards — the output should comply

### 11.2 Error messages
- Improve error reporting for unsupported F# features
- Add warnings for non-idiomatic translations

### 11.3 Module / package mapping
- F# modules → Go packages
- Namespace handling → directory structure
- Circular dependency detection (Go doesn't allow circular imports)

### 11.4 Go-specific interop attributes
- Consider adding `[<GoImport>]`, `[<GoReceiver>]` etc. to `Fable.Core`
- Allow calling existing Go libraries from F#

---

## Key Design Decisions (to resolve before/during implementation)

| # | Decision | Options | Recommendation |
|---|----------|---------|----------------|
| 1 | **Union representation** | Interface-based vs. tag-based | Interface-based (type-safe) |
| 2 | **Error handling** | panic/recover vs. error returns | panic/recover for MVP |
| 3 | **Generics** | Go 1.18+ generics vs. interface{} | Go 1.18+ (minimum Go version) |
| 4 | **Minimum Go version** | 1.18, 1.21, 1.22 | 1.21 (current LTS-ish) |
| 5 | **Async model** | Goroutines, channels, Task wrapper | Task wrapper using goroutines |
| 6 | **Immutability** | Enforce copies vs. trust convention | Trust convention (perf) |
| 7 | **Currying** | Always curry vs. optimize direct calls | Optimize direct calls |
| 8 | **AST complexity** | Minimal (PHP-like) vs. comprehensive | Start minimal, grow as needed |

---

## Suggested Implementation Order

For actually writing code, I'd suggest this ordering which optimizes for getting
something working end-to-end as early as possible:

```
Phase 0  (Scaffolding)         — Get the pipeline wired up
Phase 1  (AST Design)          — Define Go AST types
Phase 2  (Primitives)          — Basic expressions and functions
Phase 9  (String/Console)      — printf so we can see output
Phase 3  (Control Flow)        — If/else, loops, basic matching
Phase 4  (Functions)           — Closures, currying, HOFs
Phase 5  (Records & Unions)    — Core F# data types
Phase 6  (Runtime Library)     — Collections and replacements
Phase 10 (Tests)               — Start running test suite
Phase 7  (Classes/Interfaces)  — OOP features
Phase 8  (Async)               — Concurrency
Phase 11 (Refinement)          — Polish and ergonomics
```

Note: Phase 10 (tests) should really be started in parallel from Phase 2 onward,
adding tests for each feature as it's implemented.

---

## Context Management Strategy

This is a large project. To manage context across sessions:

1. **Each phase is a self-contained unit of work** — complete one before moving to the next
2. **Each phase should be a separate PR or set of commits** — makes review manageable
3. **The runtime library (Phase 6) can be developed in parallel** with the compiler phases
4. **Replacements.fs grows incrementally** — add replacements as features are implemented, not all at once
5. **Use the existing PHP backend as primary reference** — smallest, newest, clearest pattern
6. **Use Dart backend as secondary reference** — more complete type system, closer to Go's

---

## Files That Need Modification (Existing)

| File | Change |
|------|--------|
| `src/Fable.AST/Plugins.fs` | Add `Go` to `Language` DU |
| `src/Fable.Cli/Entry.fs` | Add `"go"/"golang"` CLI parsing |
| `src/Fable.Cli/Pipeline.fs` | Add `Go` module and dispatch case |
| `src/Fable.Transforms/Fable.Transforms.fsproj` | Add new Go files |
| `src/Fable.Cli/Fable.Cli.fsproj` | Possibly update if needed |
| `.github/workflows/build.yml` | Add Go test jobs |
| `.devcontainer/Dockerfile` | Add Go toolchain |

## Files to Create (New)

| File | Purpose |
|------|---------|
| `src/Fable.Transforms/Go/Go.fs` | Go AST type definitions |
| `src/Fable.Transforms/Go/Fable2Go.fs` | Fable AST → Go AST transform |
| `src/Fable.Transforms/Go/GoPrinter.fs` | Go AST → source code printer |
| `src/Fable.Transforms/Go/Replacements.fs` | Stdlib call replacements |
| `src/fable-library-go/go.mod` | Go module definition |
| `src/fable-library-go/fable_library/*.go` | Runtime library files |
| `tests/Go/Fable.Tests.Go.fsproj` | Test project |
| `tests/Go/*.fs` | Test files |
