# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Purpose

This is a Claude Code skill repository containing the **C++ Core Guidelines** by Stroustrup and Sutter. It provides knowledge for writing modern, safe, and efficient C++ code (C++11/14/17/20).

## Structure

- [SKILL.md](SKILL.md) - Main skill entry point with overview, scenario-based reference guide, and code review checklists
- [references/](references/) - Topic-specific guideline references:
  - [memory-safety.md](references/memory-safety.md) - RAII, pointers, smart pointers, const (Rules R.*, ES.*, Con.*)
  - [api-design.md](references/api-design.md) - Interfaces, function signatures, parameters (Rules I.*, F.*)
  - [class-design.md](references/class-design.md) - Classes, hierarchies, rule of zero/five (Rules C.*)
  - [concurrency.md](references/concurrency.md) - Thread safety, data races, locks (Rules CP.*)
  - [error-handling.md](references/error-handling.md) - Exceptions, error codes (Rules E.*)
  - [templates.md](references/templates.md) - Templates, STL, generics (Rules T.*, SL.*, Enum.*)
  - [modern-style.md](references/modern-style.md) - Philosophy, naming, style (Rules P.*, NL.*, SF.*)
  - [performance.md](references/performance.md) - Optimization, C compatibility (Rules Per.*, CPL.*, A.*)

## Using This Skill

When working with C++ code, load the appropriate reference file based on the task:

| Task | Reference |
|------|-----------|
| Memory leaks, RAII, pointers | [memory-safety.md](references/memory-safety.md) |
| Function signatures, APIs | [api-design.md](references/api-design.md) |
| Class design, constructors | [class-design.md](references/class-design.md) |
| Thread safety, concurrency | [concurrency.md](references/concurrency.md) |
| Exceptions, error handling | [error-handling.md](references/error-handling.md) |
| Templates, STL | [templates.md](references/templates.md) |
| Coding style, philosophy | [modern-style.md](references/modern-style.md) |
| Performance | [performance.md](references/performance.md) |

## Core Principles

The guidelines aim for code that is:
- **Type-safe**: No implicit type system violations
- **Resource-safe**: No leaks (memory, handles, locks)
- **Performant**: Efficient without sacrificing correctness
- **Correct**: Catches more logic errors at compile time
