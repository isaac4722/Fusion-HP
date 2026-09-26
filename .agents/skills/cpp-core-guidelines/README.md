# C++ Core Guidelines Skill

A Claude Code skill repository containing the **C++ Core Guidelines** by Bjarne Stroustrup and Herb Sutter. This skill provides comprehensive guidance for writing modern, safe, and efficient C++ code (C++11/14/17/20).

## Overview

The C++ Core Guidelines are a set of tried-and-true guidelines, rules, and best practices about coding in C++. They aim to help programmers write code that is:

- **Type-safe**: No implicit type system violations
- **Resource-safe**: No leaks (memory, handles, locks)
- **Performant**: Efficient without sacrificing correctness
- **Correct**: Catches more logic errors at compile time

## Structure

```
├── SKILL.md              # Main skill entry point
├── CLAUDE.md             # Repository guidance for Claude Code
└── references/           # Topic-specific guideline references
    ├── memory-safety.md  # RAII, pointers, smart pointers
    ├── api-design.md     # Interfaces, function signatures
    ├── class-design.md   # Classes, hierarchies, rule of zero/five
    ├── concurrency.md    # Thread safety, data races
    ├── error-handling.md # Exceptions, error codes
    ├── templates.md      # Templates, STL, generics
    ├── modern-style.md   # Philosophy, naming, style
    └── performance.md    # Optimization, C compatibility
```

## Usage

This skill is designed to be used with Claude Code. When working with C++ code, Claude will automatically reference the appropriate guidelines based on the task:

| Task | Reference |
|------|-----------|
| Memory leaks, RAII, pointers | `references/memory-safety.md` |
| Function signatures, APIs | `references/api-design.md` |
| Class design, constructors | `references/class-design.md` |
| Thread safety, concurrency | `references/concurrency.md` |
| Exceptions, error handling | `references/error-handling.md` |
| Templates, STL | `references/templates.md` |
| Coding style, philosophy | `references/modern-style.md` |
| Performance | `references/performance.md` |

## Guideline Categories

The guidelines are organized by rule prefixes:

| Prefix | Category |
|--------|----------|
| P | Philosophy |
| I | Interfaces |
| F | Functions |
| C | Classes and Class Hierarchies |
| R | Resource Management |
| ES | Expressions and Statements |
| Per | Performance |
| CP | Concurrency |
| E | Error Handling |
| T | Templates |
| SL | Standard Library |
| NL | Naming and Layout |

## License

The C++ Core Guidelines are maintained by the C++ community and are available under appropriate open source terms. See the [official repository](https://github.com/isocpp/CppCoreGuidelines) for more information.
