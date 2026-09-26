# Modern C++ Style and Philosophy

Use this reference for general coding philosophy, naming conventions, and file organization.

## Quick Checklist
- [ ] Intent expressed in code
- [ ] Consistent naming
- [ ] Proper header organization
- [ ] No C-style patterns

---

## P: Philosophy

### P.1: Express ideas directly in code

**Reason**: Compilers don't read comments (or design documents) and neither do many programmers (consistently). What is expressed in code has defined semantics and can be checked by compilers and other tools.

**Example (bad)**:
```cpp
void f(vector<string>& v) {
    string val;
    cin >> val;
    int index = -1;  // bad
    for (int i = 0; i < v.size(); ++i) {
        if (v[i] == val) {
            index = i;
            break;
        }
    }
}
```

**Example (good)**:
```cpp
void f(vector<string>& v) {
    string val;
    cin >> val;
    auto p = find(begin(v), end(v), val);  // better
}
```

**Enforcement**: Use `const` consistently; flag uses of casts; detect code that mimics the standard library.

---

### P.2: Write in ISO Standard C++

**Reason**: This is a set of guidelines for writing ISO Standard C++.

**Enforcement**: Use an up-to-date C++ compiler (C++20 or C++17) with options that do not accept extensions.

---

### P.3: Express intent

**Reason**: Unless the intent of some code is stated (e.g., in names or comments), it is impossible to tell whether the code does what it is supposed to do.

**Example (bad)**:
```cpp
gsl::index i = 0;
while (i < v.size()) {
    // ... do something with v[i] ...
}
```

**Example (good)**:
```cpp
for (const auto& x : v) { /* do something with x */ }
```

**Enforcement**: Look for simple `for` loops vs range-`for` loops; `f(T*, int)` interfaces vs `f(span<T>)`; loop variables in too large a scope; naked `new` and `delete`.

---

### P.4: Ideally, a program should be statically type safe

**Reason**: Ideally, a program would be completely statically (compile-time) type safe. Problem areas: unions, casts, array decay, range errors, narrowing conversions.

**Enforcement**:
- unions -- use `variant`
- casts -- minimize their use
- array decay -- use `span`
- range errors -- use `span`
- narrowing conversions -- use `narrow` or `narrow_cast`

---

### P.5: Prefer compile-time checking to run-time checking

**Reason**: Code clarity and performance. You don't need to write error handlers for errors caught at compile time.

**Example (bad)**:
```cpp
int bits = 0;
for (Int i = 1; i; i <<= 1)
    ++bits;
if (bits < 32)
    cerr << "Int too small\n";
```

**Example (good)**:
```cpp
static_assert(sizeof(Int) >= 4);
```

**Enforcement**: Look for pointer arguments; look for run-time checks for range violations.

---

### P.6: What cannot be checked at compile time should be checkable at run time

**Reason**: Leaving hard-to-detect errors in a program is asking for crashes and bad results.

**Example (bad)**:
```cpp
extern void f(int* p);
void g(int n) {
    f(new int[n]);  // bad: number of elements not passed
}
```

**Example (good)**:
```cpp
extern void f4(vector<int>&);
void g3(int n) {
    vector<int> v(n);
    f4(v);
}
```

**Enforcement**: Flag (pointer, count)-style interfaces.

---

### P.7: Catch run-time errors early

**Reason**: Avoid "mysterious" crashes. Avoid errors leading to wrong results.

**Example (bad)**:
```cpp
void increment1(int* p, int n) {
    for (int i = 0; i < n; ++i) ++p[i];
}
```

**Example (good)**:
```cpp
void increment2(span<int> p) {
    for (int& x : p) ++x;
}
```

**Enforcement**: Look at pointers and arrays: Do range-checking early; look at conversions: Eliminate narrowing conversions; look for unchecked values from input.

---

### P.8: Don't leak any resources

**Reason**: Even a slow growth in resources will, over time, exhaust availability.

**Example (bad)**:
```cpp
void f(const char* name) {
    FILE* input = fopen(name, "r");
    if (something) return;   // bad: leak
    fclose(input);
}
```

**Example (good)**:
```cpp
void f(const char* name) {
    ifstream input {name};
    if (something) return;   // OK: no leak
}
```

**Enforcement**: Classify pointers into non-owners and owners; look for naked `new` and `delete`; look for resource allocating functions returning raw pointers.

---

### P.9: Don't waste time or space

**Reason**: This is C++.

**Enforcement**: Flag unused return value from postfix `operator++` or `operator--`.

---

### P.10: Prefer immutable data to mutable data

**Reason**: It is easier to reason about constants than about variables. Something immutable cannot change unexpectedly. You can't have a data race on a constant.

---

### P.11: Encapsulate messy constructs, rather than spreading through the code

**Reason**: Messy code is more likely to hide bugs and harder to write. A good interface is easier and safer to use.

**Example (bad)**:
```cpp
int sz = 100;
int* p = (int*) malloc(sizeof(int) * sz);
// manual reallocation and error checking...
```

**Example (good)**:
```cpp
vector<int> v;
v.reserve(100);
for (int x; cin >> x; ) {
    v.push_back(x);
}
```

**Enforcement**: Look for "messy code" such as complex pointer manipulation outside abstractions.

---

### P.12: Use supporting tools as appropriate

**Reason**: There are many things done better "by machine." Computers don't tire or get bored by repetitive tasks.

---

### P.13: Use support libraries as appropriate

**Reason**: Using a well-designed, well-documented library saves time and effort.

**Example**:
```cpp
std::sort(begin(v), end(v), std::greater<>());
```

---

## SF: Source Files

Distinguish between declarations (used as interfaces) and definitions (used as implementations). Use header files to represent interfaces and to emphasize logical structure.

### SF.2: A header file must not contain object definitions or non-inline function definitions

**Reason**: Including entities subject to the one-definition rule leads to linkage errors.

**Example (bad)**:
```cpp
// file.h:
namespace Foo {
    int x = 7;
    int xx() { return x+x; }
}
// Linking file1.cpp and file2.cpp (both including file.h) will give two linker errors.
```

A header file must contain only:
- `#include`s of other header files
- templates
- class definitions
- function declarations
- `extern` declarations
- `inline` function definitions
- `constexpr` definitions
- `const` definitions
- `using` alias definitions

**Enforcement**: Check the positive list above.

---

### SF.3: Use header files for all declarations used in multiple source files

**Reason**: Maintainability. Readability.

**Example (bad)**:
```cpp
// bar.cpp:
void bar() { cout << "bar\n"; }

// foo.cpp:
extern void bar();  // A maintainer cannot find all declarations of bar
void foo() { bar(); }
```

**Enforcement**: Flag declarations of entities in other source files not placed in a `.h`.

---

### SF.4: Include header files before other declarations in a file

**Reason**: Minimize context dependencies and increase readability.

**Example**:
```cpp
#include <vector>
#include <algorithm>
#include <string>

// ... my code here ...
```

---

### SF.5: A `.cpp` file must include the header file(s) that defines its interface

**Reason**: This enables the compiler to do an early consistency check.

**Example (bad)**:
```cpp
// foo.h:
void foo(int);
int bar(long);
int foobar(int);

// foo.cpp:
void foo(int) { /* ... */ }
int bar(double) { /* ... */ }  // Error not caught until link time
double foobar(int);
```

**Example (good)**:
```cpp
// foo.cpp:
#include "foo.h"

void foo(int) { /* ... */ }
int bar(double) { /* ... */ }
double foobar(int);   // error: wrong return type (caught at compile time)
```

---

### SF.6: Use `using namespace` directives for transition, for foundation libraries (such as `std`), or within a local scope (only)

**Reason**: `using namespace` can lead to name clashes, so it should be used sparingly.

---

### SF.7: Don't write `using namespace` at global scope in a header file

**Reason**: Doing so takes away an `#include`r's ability to effectively disambiguate and to use alternatives. It also makes `#include`d headers order-dependent.

**Example (bad)**:
```cpp
// bad.h
#include <iostream>
using namespace std; // bad

// user.cpp
#include "bad.h"
bool copy(/*...*/);    // some function that happens to be named copy
int main() {
    copy(/*...*/);    // now overloads local ::copy and std::copy, could be ambiguous
}
```

**Enforcement**: Flag `using namespace` at global scope in a header file.

---

### SF.8: Use `#include` guards for all header files

**Reason**: To avoid files being `#include`d several times. To avoid include guard collisions, do not just name the guard after the filename--include the name of library or component.

**Example**:
```cpp
// file foobar.h:
#ifndef LIBRARY_FOOBAR_H
#define LIBRARY_FOOBAR_H
// ... declarations ...
#endif // LIBRARY_FOOBAR_H
```

**Enforcement**: Flag `.h` files without `#include` guards.

**Note**: `#pragma once` is not standard and not portable.

---

### SF.9: Avoid cyclic dependencies among source files

**Reason**: Cycles complicate comprehension and slow down compilation. They also complicate conversion to use language-supported modules.

**Note**: Eliminate cycles; don't just break them with `#include` guards.

**Enforcement**: Flag all cycles.

---

### SF.10: Avoid dependencies on implicitly `#include`d names

**Reason**: Avoid surprises and having to change `#include`s if an `#include`d header changes.

**Example (bad)**:
```cpp
#include <iostream>
using namespace std;

void use() {
    string s;
    cin >> s;               // fine
    getline(cin, s);        // error: getline() not defined
    if (s == "surprise") {  // error: == not defined
```

`<iostream>` may expose `std::string`, but it's not required to. Explicitly `#include <string>`.

---

### SF.11: Header files should be self-contained

**Reason**: Usability--headers should be simple to use and work when included on their own. Headers should encapsulate the functionality they provide.

**Example**:
```cpp
#include "helpers.h";
// helpers.h depends on std::string and includes <string>
```

---

### SF.12: Prefer the quoted form of `#include` for files relative to the including file and the angle bracket form everywhere else

**Reason**: Encourages clarity about whether a header is local relative or from a library.

**Example**:
```cpp
// foo.cpp:
#include <string>                // From the standard library
#include <some_library/common.h> // From another library
#include "foo.h"                 // Local relative file
#include "util/util.h"           // Local relative file
```

---

### SF.13: Use portable header identifiers in `#include` statements

**Reason**: To maximize portability across compilers.

**Guidance**:
- Use case-sensitivity matching how the header is defined
- Use forward-slash `/` to delimit path components

**Example**:
```cpp
#include <vector>       // good
#include <String>       // bad: should be <string>
#include "util/util.h"  // good
#include "util\util.h"  // bad: backslash not portable
```

---

### SF.21: Don't use an unnamed (anonymous) namespace in a header

**Reason**: It is almost always a bug to mention an unnamed namespace in a header file.

**Example (bad)**:
```cpp
// file foo.h:
namespace {
    const double x = 1.234;  // bad
    double foo(double y)     // bad
    {
        return y + x;
    }
}
```

**Example (good)**:
```cpp
// file foo.h:
namespace Foo {
    const double x = 1.234; // good
    inline double foo(double y)  // good
    {
        return y + x;
    }
}
```

**Enforcement**: Flag any use of an anonymous namespace in a header file.

---

### SF.22: Use an unnamed (anonymous) namespace for all internal/non-exported entities

**Reason**: Nothing external can depend on an entity in a nested unnamed namespace.

**Example (bad)**:
```cpp
static int f();
int g();
static bool h();
int k();
```

**Example (good)**:
```cpp
namespace {
    int f();
    bool h();
}
int g();
int k();
```

---

## NL: Naming and Layout (Key Rules)

Consistent naming and layout are helpful. The following are the most important rules.

### NL.5: Avoid encoding type information in names

**Reason**: If names reflect types rather than functionality, it becomes hard to change the types used.

**Example (bad)**:
```cpp
void print_int(int i);
void print_string(const char*);
print_int(1);
print_string("xyzzy");
```

**Example (good)**:
```cpp
void print(int i);
void print(string_view);
print(1);
print("xyzzy");
```

**Note**: This doesn't prohibit simple prefixes like `p` for pointer or `cnt` for count, which indicate general usage rather than specific type.

---

### NL.7: Make the length of a name roughly proportional to the length of its scope

**Reason**: The larger the scope, the greater the chance of confusion and unintended name clash.

**Example**:
```cpp
double sqrt(double x);   // short name, short scope (parameter)
int open;   // bad: global variable with short, popular name
```

---

### NL.8: Use a consistent naming style

**Reason**: Consistency in naming and naming style increases readability.

**Common styles**:
- ISO Standard: `int`, `vector`, `my_map` (lowercase with underscores)
- Stroustrup: `int`, `vector`, `My_map` (user types capitalized)
- CamelCase: `int`, `vector`, `MyMap`, `myMap`

**Avoid**: Identifiers with double underscores `__` or starting with underscore + capital letter (reserved for implementation).

---

### NL.9: Use `ALL_CAPS` for macro names only

**Reason**: To avoid confusing macros with names that obey scope and type rules.

**Example (bad)**:
```cpp
void f() {
    const int SIZE{100};  // Bad, use 'size' instead
    int v[SIZE];
}
```

**Also**: Don't use `ALL_CAPS` for enumerators:
```cpp
enum bad { BAD, WORSE, HORRIBLE }; // BAD
```

**Enforcement**: Flag macros with lower-case letters; flag `ALL_CAPS` non-macro names.

---

### NL.10: Prefer `underscore_style` names

**Reason**: The use of underscores to separate parts of a name is the original C and C++ style and used in the C++ Standard Library.

**Note**: This is a default to use only if you have a choice. Consistency with existing code beats personal taste.

---

### NL.11: Make literals readable

**Reason**: Readability. It's easy to make a typo in a long string of integers.

**Example**:
```cpp
auto c = 299'792'458;      // m/s
auto q2 = 0b0000'1111'0000'0000;
auto ss_number = 123'456'7890;
auto hello = "Hello!"s;   // std::string
auto interval = 100ms;    // using <chrono>
```

**Enforcement**: Flag long digit sequences (7+ digits).

---

### NL.16: Use a conventional class member declaration order

**Reason**: A conventional order of members improves readability.

**Order**:
1. types: classes, enums, and aliases (`using`)
2. constructors, assignments, destructor
3. functions
4. data

Use `public` before `protected` before `private` order.

**Example**:
```cpp
class X {
public:
    // interface
protected:
    // unchecked function for use by derived class implementations
private:
    // implementation details
};
```

**Avoid**: Multiple blocks of declarations of one access level dispersed among blocks of different access levels.

---

### NL.17: Use K&R-derived layout

**Reason**: This is the original C and C++ layout. It preserves vertical space well and distinguishes different language constructs.

**Note**: Often called "Stroustrup" style in C++ context.

**Key points**:
- `{` for a `class` and `struct` is NOT on a separate line
- `{` for a function IS on a separate line
- Space between `if` and `(`
- Separate lines for each statement and branch

---

### NL.18: Use C++-style declarator layout

**Reason**: The C++-style emphasizes types, which is more appropriate when references are involved.

**Example**:
```cpp
T& operator[](size_t);   // OK
T &operator[](size_t);   // just strange
T & operator[](size_t);   // undecided
```

---

### NL.19: Avoid names that are easily misread

**Reason**: Readability. Not everyone has screens that make it easy to distinguish all characters.

**Example**:
```cpp
int oO01lL = 6; // bad

int splunk = 7;
int splonk = 8; // bad: easily confused
```

---

### NL.20: Don't place two statements on the same line

**Reason**: Readability. It is easy to overlook a statement when there is more on a line.

**Example (bad)**:
```cpp
int x = 7; char* p = 29;    // don't
int x = 7; f(x);  ++x;      // don't
```

---

### NL.21: Declare one name (only) per declaration

**Reason**: Readability and minimizing confusion with declarator syntax.

---

### NL.25: Don't use `void` as an argument type

**Reason**: It's verbose and only needed where C compatibility matters.

**Example**:
```cpp
void f(void);   // bad
void g();       // better
```

---

### NL.26: Use conventional `const` notation

**Reason**: Conventional notation is more familiar to more programmers. Consistency in large code bases.

**Example**:
```cpp
const int x = 7;    // OK
int const y = 9;    // bad

const int *const p = nullptr;   // OK
int const *const p = nullptr;   // bad
```

**Enforcement**: Flag `const` used as a suffix for a type.

---

### NL.27: Use a `.cpp` suffix for code files and `.h` for interface files

**Reason**: It's a longstanding convention. Consistency is more important, so if your project uses something else, follow that.

**Example**:
```cpp
// foo.h:
extern int a;   // a declaration
extern void foo();

// foo.cpp:
int a;   // a definition
void foo() { ++a; }
```

**Note**: `.h` provides the interface to `.cpp`. Global variables should still be avoided.

**Enforcement**: Flag non-conventional file names; check that `.h` and `.cpp` follow the rules.
