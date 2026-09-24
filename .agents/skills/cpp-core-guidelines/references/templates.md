# Templates, Generics, and Standard Library

Use this reference when writing templates, using STL, or working with generic code.

## Quick Checklist
- [ ] Concepts constrain templates
- [ ] STL algorithms preferred over raw loops
- [ ] Proper use of standard containers
- [ ] Enum classes, not plain enums

---

## T: Templates and Generic Programming

### T.1: Use templates to raise the level of abstraction of code

**Reason**: Generality. Reuse. Efficiency. Encourages consistent definition of user types.

**Example** (good):
```cpp
template<typename T>
    requires Arithmetic<T>
T sum(vector<T>& v, T s)
{
    for (auto x : v) s += x;
    return s;
}
```

**Enforcement**: Flag algorithms with "overly simple" requirements without concepts.

---

### T.2: Use templates to express algorithms that apply to many argument types

**Reason**: Generality. Minimizing source code. Interoperability. Reuse.

**Example**:
```cpp
template<typename Iter, typename Val>
Iter find(Iter b, Iter e, Val v)
{
    // ...
}
```

**Enforcement**: Don't use a template unless you have a realistic need for more than one template argument type.

---

### T.3: Use templates to express containers and ranges

**Reason**: Containers need an element type, and expressing that as a template argument is general, reusable, and type safe.

**Example** (good):
```cpp
template<typename T>
class Vector {
    T* elem;
    int sz;
};
```

**Example** (bad):
```cpp
class Container {
    void* elem;
    int sz;
};
```

**Enforcement**: Flag uses of `void*`s and casts outside low-level implementation code.

---

### T.4: Use templates to express syntax tree manipulation

**Reason**: ???

---

### T.5: Combine generic and OO techniques to amplify their strengths, not their costs

**Reason**: Generic and OO techniques are complementary.

**Example** (static helps dynamic):
```cpp
class Command { /* pure virtual functions */ };

template</*...*/>
class ConcreteCommand : public Command {
    // implement virtuals
};
```

**Example** (dynamic helps static - type erasure):
```cpp
class Object {
public:
    template<typename T>
    Object(T&& obj)
        : concept_(std::make_shared<ConcreteCommand<T>>(std::forward<T>(obj)) {}
    int get_id() const { return concept_->get_id(); }
private:
    struct Command {
        virtual ~Command() {}
        virtual int get_id() const = 0;
    };
    template<typename T>
    struct ConcreteCommand final : Command {
        T object_;
        int get_id() const final { return object_.get_id(); }
    };
    std::shared_ptr<Command> concept_;
};
```

---

### T.10: Specify concepts for all template arguments

**Reason**: Correctness and readability. The assumed meaning of a template argument is fundamental to the interface of a template.

**Example**:
```cpp
template<input_iterator Iter, typename Val>
    requires equality_comparable_with<iter_value_t<Iter>, Val>
Iter find(Iter b, Iter e, Val v)
{
    // ...
}
```

**Enforcement**: Flag template type arguments without concepts.

---

### T.11: Whenever possible use standard concepts

**Reason**: "Standard" concepts save work, are better thought out, and improve interoperability.

**Example** (bad):
```cpp
template<typename T>
concept Ordered_container = Sequence<T> && Random_access<Iterator<T>> && Ordered<Value_type<T>>;
void sort(Ordered_container auto& s);
```

**Example** (good):
```cpp
void sort(sortable auto& s);   // better
```

**Enforcement**: Look for unconstrained arguments, unusual/non-standard concepts, homebrew concepts without axioms.

---

### T.12: Prefer concept names over `auto` for local variables

**Reason**: `auto` is the weakest concept. Concept names convey more meaning.

**Example**:
```cpp
vector<string> v{ "abc", "xyz" };
auto& x = v.front();        // bad
String auto& s = v.front(); // good
```

---

### T.13: Prefer the shorthand notation for simple, single-type argument concepts

**Reason**: Readability. Direct expression of an idea.

**Example**:
```cpp
// Verbose
template<typename T>
    requires sortable<T>
void sort(T&);

// Better
template<sortable T>
void sort(T&);

// Best
void sort(sortable auto&);
```

**Enforcement**: Later, flag declarations that introduce a typename then constrain it with a simple concept.

---

### T.20: Avoid "concepts" without meaningful semantics

**Reason**: Concepts are meant to express semantic notions. Simple constraints like "has a +" cannot be meaningfully specified in isolation.

**Example** (bad):
```cpp
template<typename T>
concept Addable = requires(T a, T b) { a + b; };
```

**Example** (good):
```cpp
template<typename T>
concept Number = requires(T a, T b) { a + b; a - b; a * b; a / b; };
```

**Enforcement**: Flag single-operation concepts when used outside other concept definitions.

---

### T.21: Require a complete set of operations for a concept

**Reason**: Ease of comprehension. Improved interoperability. Helps implementers and maintainers.

**Example** (bad):
```cpp
template<typename T> concept Subtractable = requires(T a, T b) { a - b; };
```

Complete sets: `Arithmetic` (+, -, *, /, +=, -=, *=, /=), `Comparable` (<, >, <=, >=, ==, !=)

**Enforcement**: Flag classes that support "odd" subsets of operators.

---

### T.22: Specify axioms for concepts

**Reason**: A meaningful/useful concept has semantic meaning. Expressing semantics makes the concept comprehensible.

**Example**:
```cpp
template<typename T>
    // axiom(T a, T b) { a + b == b + a; a - a == 0; /*...*/ }
    concept Number = requires(T a, T b) {
        { a + b } -> convertible_to<T>;
        { a - b } -> convertible_to<T>;
        { a * b } -> convertible_to<T>;
        { a / b } -> convertible_to<T>;
    };
```

**Enforcement**: Look for the word "axiom" in concept definition comments.

---

### T.23: Differentiate a refined concept from its more general case by adding new use patterns

**Reason**: Otherwise they cannot be distinguished automatically by the compiler.

**Example**:
```cpp
template<typename I>
concept Input_iter = requires(I iter) { ++iter; };

template<typename I>
concept Fwd_iter = Input_iter<I> && requires(I iter) { iter++; };
```

**Enforcement**: Flag a concept with exactly the same requirements as another.

---

### T.24: Use tag classes or traits to differentiate concepts that differ only in semantics

**Reason**: Two concepts requiring same syntax but different semantics lead to ambiguity.

**Example**:
```cpp
template<typename I>
concept Contiguous_iter =
    RA_iter<I> && is_contiguous_v<I>;  // using trait
```

---

### T.25: Avoid complementary constraints

**Reason**: Clarity. Maintainability. Functions with complementary requirements are brittle.

**Example** (bad):
```cpp
template<typename T>
    requires !C<T>    // bad
void f();

template<typename T>
    requires C<T>
void f();
```

**Example** (good):
```cpp
template<typename T>   // general template
void f();

template<typename T>   // specialization by concept
    requires C<T>
void f();
```

**Enforcement**: Flag pairs of functions with `C<T>` and `!C<T>` constraints.

---

### T.26: Prefer to define concepts in terms of use-patterns rather than simple syntax

**Reason**: Definition is more readable and corresponds directly to what users write.

**Example**:
```cpp
template<typename T> concept Equality = requires(T a, T b) {
    { a == b } -> std::convertible_to<bool>;
    { a != b } -> std::convertible_to<bool>;
};
```

---

### T.40: Use function objects to pass operations to algorithms

**Reason**: Function objects can carry more information and typically give better performance.

**Example**:
```cpp
sort(v, greater());                                    // pointer to function: potentially slow
sort(v, [](double x, double y) { return x > y; });   // function object
sort(v, std::greater{});                             // function object
```

**Enforcement**: Flag pointer to function template arguments.

---

### T.41: Require only essential properties in a template's concepts

**Reason**: Keep interfaces simple and stable.

**Note**: If we require every operation used to be listed, interfaces become unstable.

---

### T.42: Use template aliases to simplify notation and hide implementation details

**Reason**: Improved readability. Implementation hiding.

**Example**:
```cpp
template<typename T, size_t N>
class Matrix {
    using Iterator = typename std::vector<T>::iterator;
};

template<typename T>
using Value_type = typename container_traits<T>::value_type;
```

**Enforcement**: Flag use of `typename` as disambiguator outside `using` declarations.

---

### T.43: Prefer `using` over `typedef` for defining aliases

**Reason**: Improved readability, generality (template aliases), uniformity.

**Example**:
```cpp
typedef int (*PFI)(int);   // OK, but convoluted
using PFI2 = int (*)(int); // OK, preferred

template<typename T>
using PFT2 = int (*)(T);   // OK
```

**Enforcement**: Flag uses of `typedef`.

---

### T.44: Use function templates to deduce class template argument types (where feasible)

**Reason**: Writing template argument types explicitly can be tedious and verbose.

**Example**:
```cpp
tuple<int, string, double> t1 = {1, "Hamlet", 3.14};   // explicit
auto t2 = make_tuple(1, "Ophelia"s, 3.14);            // deduced
```

**Note**: C++17 allows template argument deduction directly from constructors.

---

### T.46: Require template arguments to be at least semiregular

**Reason**: Readability. Preventing surprises.

**Example**:
```cpp
class X {
public:
    explicit X(int);
    X(const X&);
    X operator=(const X&);
    // ... no default constructor
};

std::vector<X> v(10); // error: no default constructor
```

**Enforcement**: Flag types used as template arguments that are not at least semiregular.

---

### T.47: Avoid highly visible unconstrained templates with common names

**Reason**: An unconstrained template is a perfect match for anything and can be preferred over more specific types.

**Example** (problematic):
```cpp
namespace Bad {
    struct S { int m; };
    template<typename T1, typename T2>
    bool operator==(T1, T2) { cout << "Bad\n"; return true; }
}
```

**Enforcement**: Flag templates defined in a namespace where concrete types are also defined.

---

### T.48: If your compiler does not support concepts, fake them with `enable_if`

**Reason**: That's the best we can do without direct concept support.

**Example**:
```cpp
template<typename T>
enable_if_t<is_integral_v<T>>
f(T v) { /* ... */ }

// Equivalent to:
template<Integral T>
void f(T v) { /* ... */ }
```

**Note**: Beware of complementary constraints.

---

### T.49: Where possible, avoid type-erasure

**Reason**: Type erasure incurs an extra level of indirection by hiding type information.

**Exception**: Type erasure is sometimes appropriate, such as for `std::function`.

---

### T.60: Minimize a template's context dependencies

**Reason**: Eases understanding. Minimizes errors from unexpected dependencies.

**Example** (bad):
```cpp
template<typename Iter>
Iter algo(Iter first, Iter last)
{
    for (; first != last; ++first) {
        auto x = sqrt(*first); // potentially surprising dependency
        helper(first, x);      // potentially surprising dependency
    }
}
```

---

### T.61: Do not over-parameterize members (SCARY)

**Reason**: A member that does not depend on a template parameter limits use and increases code size.

**Example** (bad):
```cpp
template<typename T, typename A = std::allocator<T>>
class List {
public:
    struct Link {   // does not depend on A
        T elem;
        Link* pre;
        Link* suc;
    };
};
```

**Example** (good):
```cpp
template<typename T>
struct Link {
    T elem;
    Link* pre;
    Link* suc;
};

template<typename T, typename A = std::allocator<T>>
class List2 {
    using iterator = Link<T>*;
};
```

**Enforcement**: Flag member types/functions/lambdas that don't depend on every template parameter.

---

### T.62: Place non-dependent class template members in a non-templated base class

**Reason**: Allow base class members to be used without template instantiation.

**Example**:
```cpp
struct Foo_base {
    enum { v1, v2 };
};

template<typename T>
class Foo : public Foo_base {
    // ...
};
```

---

### T.64: Use specialization to provide alternative implementations of class templates

**Reason**: A template defines a general interface. Specialization offers alternative implementations.

---

### T.65: Use tag dispatch to provide alternative implementations of a function

**Reason**: Tag dispatch allows selecting implementations based on type properties. Performance.

**Example**:
```cpp
struct trivially_copyable_tag {};
struct non_trivially_copyable_tag {};

template<class T> struct copy_trait { using tag = non_trivially_copyable_tag; };
template<> struct copy_trait<int> { using tag = trivially_copyable_tag; };

template<class Iter>
Out copy_helper(Iter first, Iter last, Iter out, trivially_copyable_tag) {
    // use memmove
}

template<class Iter>
Out copy_helper(Iter first, Iter last, Iter out, non_trivially_copyable_tag) {
    // use loop
}

template<class Iter>
Out copy(Iter first, Iter last, Iter out) {
    using tag_type = typename copy_trait<std::iter_value_t<Iter>>::tag;
    return copy_helper(first, last, out, tag_type{});
}
```

---

### T.68: Use `{}` rather than `()` within templates to avoid ambiguities

**Reason**: `()` is vulnerable to grammar ambiguities.

**Example**:
```cpp
template<typename T, typename U>
void f(T t, U u)
{
    T v1(T(u));    // mistake: v1 is a function
    T v2{u};       // clear: a variable
}
```

**Enforcement**: Flag `()` initializers and function-style casts in templates.

---

### T.69: Inside a template, don't make an unqualified non-member function call unless you intend it to be a customization point

**Reason**: Provide only intended flexibility. Avoid vulnerability to environmental changes.

**Note**: If calling your own helper `helper(t)` with dependent `t`, put it in `::detail` namespace and qualify as `detail::helper(t)`.

**Enforcement**: Flag unqualified call to non-member function passing dependent type when there's a same-name function in template's namespace.

---

### T.80: Do not naively templatize a class hierarchy

**Reason**: Templating a class hierarchy with many virtual functions can lead to code bloat.

**Example** (bad):
```cpp
template<typename T>
struct Container {
    virtual T* get(int i);
    virtual void sort();
};

template<typename T>
class Vector : public Container<T> {
    // Compiler must generate code for all virtual functions
};
```

**Enforcement**: Flag virtual functions that depend on template argument.

---

### T.81: Do not mix hierarchies and arrays

**Reason**: An array of derived classes can decay to pointer to base with disastrous results.

**Example** (horrible):
```cpp
void maul(Fruit* p) {
    *p = Pear{};
    p[1] = Pear{};
}

Apple aa[] = { an_apple, another_apple };
maul(aa);  // type violation and memory corruption
```

**Alternative**: Use proper (templatized) container like `vector<Apple>`.

---

### T.83: Do not declare a member function template virtual

**Reason**: C++ does not support this. If it did, vtbls couldn't be generated until link time.

**Example**:
```cpp
class Shape {
    template<class T>
    virtual bool intersect(T* p);   // error: template cannot be virtual
};
```

**Enforcement**: The compiler handles this.

---

### T.84: Use a non-template core implementation to provide an ABI-stable interface

**Reason**: Improve stability. Avoid code bloat.

**Example**:
```cpp
struct Link_base {   // stable
    Link_base* suc;
    Link_base* pre;
};

template<typename T>   // templated wrapper
struct Link : Link_base {
    T val;
};
```

---

### T.100: Use variadic templates when you need a function that takes a variable number of arguments of a variety of types

**Reason**: Variadic templates is the most general mechanism, efficient and type-safe. Don't use C varargs.

**Enforcement**: Flag uses of `va_arg` in user code.

---

### T.120: Use template metaprogramming only when you really need to

**Reason**: TMP is hard to get right, slows compilation, and is hard to maintain.

**Note**: If result is a value rather than a type, use `constexpr` function.

---

### T.121: Use template metaprogramming primarily to emulate concepts

**Reason**: Where C++20 is not available, we need to emulate concepts.

**Example**:
```cpp
template<typename Iter>
enable_if<random_access_iterator<Iter>, void>
advance(Iter p, int n) { p += n; }
```

**Note**: Such code is much simpler using actual concepts.

---

### T.122: Use templates (usually template aliases) to compute types at compile time

**Reason**: TMP is the only supported way of generating types at compile time.

---

### T.123: Use `constexpr` functions to compute values at compile time

**Reason**: A function is the most obvious way to express computation.

**Example**:
```cpp
template<typename T>
constexpr T pow(T v, int n)
{
    T res = 1;
    while (n--) res *= v;
    return res;
}

constexpr auto f7 = pow(pi, 7);
```

**Enforcement**: Flag template metaprograms yielding a value.

---

### T.143: Don't write unintentionally non-generic code

**Reason**: Generality. Reusability. Use the most general facilities available.

**Example**:
```cpp
for (auto i = first; i < last; ++i) {   // less generic
    // ...
}

for (auto i = first; i != last; ++i) {   // more generic
    // ...
}
```

**Enforcement**: Flag functions taking pointer/reference to more-derived type but only using base functions.

---

### T.144: Don't specialize function templates

**Reason**: You can't partially specialize function templates. Specializations don't participate in overloading.

**Exception**: Delegate to a class template that you can specialize properly.

**Enforcement**: Flag all specializations of function templates. Overload instead.

---

### T.150: Check that a class matches a concept using `static_assert`

**Reason**: If you intend a class to match a concept, verifying early saves users' pain.

**Example**:
```cpp
static_assert(Default_constructible<X>);    // error if not
static_assert(Copyable<X>);                 // error if not
```

---

## SL: Standard Library

### SL.1: Use libraries wherever possible

**Reason**: Save time. Don't re-invent the wheel. Don't replicate work of others.

---

### SL.2: Prefer the standard library to other libraries

**Reason**: More people know it. More likely to be stable, well-maintained, widely available.

---

### SL.3: Do not add non-standard entities to namespace `std`

**Reason**: Adding to `std` might change meaning of conforming code. May clash with future standard.

**Example** (bad):
```cpp
namespace std { // BAD
    class My_vector { /* ... */ };
}
```

**Example** (good):
```cpp
namespace Foo { // GOOD
    class My_vector { /* ... */ };
}
```

**Enforcement**: Possible but messy and likely to cause problems.

---

### SL.4: Use the standard library in a type-safe manner

**Reason**: Breaking this leads to undefined behavior, memory corruption, and security violations.

---

### SL.con.1: Prefer using STL `array` or `vector` instead of a C array

**Reason**: C arrays are less safe. `array` doesn't degenerate to pointer. `vector` handles memory allocation.

**Example** (bad):
```cpp
int v[SIZE];           // BAD
int* v = new int[n];   // BAD, owning raw pointer
```

**Example** (good):
```cpp
std::array<int, SIZE> w;          // ok
std::vector<int> w(initial_size); // ok
```

**Enforcement**: Flag C array declaration in function/class that also declares STL container.

---

### SL.con.2: Prefer using STL `vector` by default unless you have a reason to use a different container

**Reason**: `vector` offers fastest general-purpose access, lowest space overhead.

**Note**: If size never changes, use `array` instead.

**Exception**: For dictionary-style lookup with large containers, use `unordered_map` or `map`.

**Enforcement**: Flag `vector` whose size never changes after construction.

---

### SL.con.3: Avoid bounds errors

**Reason**: Read/write beyond allocated range leads to errors, crashes, security violations.

**Example** (bad):
```cpp
void f() {
    array<int, 10> a, b;
    memset(a.data(), 0, 10);         // BAD
    v[0] = a[0];                     // BAD
}
```

**Example** (good):
```cpp
void f() {
    array<int, 10> a, b;
    a.fill(0);
    v.at(0) = a.at(0);  // bounds-checked
    if (a == b) { /* ... */ }
}
```

**Enforcement**: Issue diagnostic for any call to non-bounds-checked standard library function.

---

### SL.con.4: Don't use `memset` or `memcpy` for arguments that are not trivially-copyable

**Reason**: Doing so messes object semantics (e.g., overwrites vptr).

**Example** (bad):
```cpp
struct base {
    virtual void update() = 0;
};
void f(derived& a, derived& b) {
    memset(&a, 0, sizeof(derived));  // goodbye v-table
}
```

**Example** (good):
```cpp
void g(derived& a, derived& b) {
    a = {};    // default initialize
    b = a;     // copy
}
```

**Enforcement**: Flag use for types not trivially copyable.

---

### SL.str.1: Use `std::string` to own character sequences

**Reason**: `string` correctly handles allocation, ownership, copying, expansion.

**Example**:
```cpp
vector<string> read_until(string_view terminator) {
    vector<string> res;
    for (string s; cin >> s && s != terminator;)
        res.push_back(s);
    return res;
}
```

**Example** (bad):
```cpp
char* cat(const char* s1, const char* s2) {
    char* p = (char*) malloc(strlen(s1) + strlen(s2) + 2);
    // manual strcpy... error-prone
    return p;
}
```

---

### SL.str.2: Use `std::string_view` or `gsl::span<char>` to refer to character sequences

**Reason**: Provides simple, safe access to character sequences independently of allocation.

**Example**:
```cpp
vector<string> read_until(string_view terminator);

void user(zstring p, const string& s, string_view ss) {
    auto v1 = read_until(p);
    auto v2 = read_until(s);
    auto v3 = read_until(ss);
}
```

**Note**: `std::string_view` is read-only.

---

### SL.str.3: Use `zstring` or `czstring` to refer to a C-style, zero-terminated sequence of characters

**Reason**: Readability. Statement of intent. A plain `char*` is ambiguous.

---

### SL.str.4: Use `char*` to refer to a single character

**Reason**: Distinguishing single char from array/string prevents misunderstandings.

---

### SL.str.5: Use `std::byte` to refer to byte values that do not necessarily represent characters

**Reason**: Clearly indicates intent to work with raw bytes, not text.

---

### SL.str.10: Use `std::string` when you need to perform locale-sensitive string operations

**Reason**: `std::string` provides locale-aware operations.

---

### SL.str.11: Use `gsl::span<char>` rather than `std::string_view` when you need to mutate a string

**Reason**: `std::string_view` is read-only.

**Enforcement**: Compiler flags attempts to write to `string_view`.

---

### SL.str.12: Use the `s` suffix for string literals meant to be standard-library `string`s

**Reason**: Direct expression of intent minimizes mistakes.

**Example**:
```cpp
auto pp1 = make_pair("Tokyo", 9.00);         // {C-style string,double}
pair<string, double> pp2 = {"Tokyo", 9.00};  // verbose
auto pp3 = make_pair("Tokyo"s, 9.00);        // {std::string,double}
```

---

### SL.io.1: Use character-level input only when you have to

**Reason**: Character-level input leads to error-prone, inefficient composition.

**Example** (bad):
```cpp
char c;
char buf[128];
while (cin.get(c) && !isspace(c) && i < 128)
    buf[i++] = c;
```

**Example** (good):
```cpp
string s;
cin >> s;
```

---

### SL.io.2: When reading, always consider ill-formed input

**Reason**: Errors best handled as soon as possible. Every function shouldn't cope with bad data.

---

### SL.io.3: Prefer `iostream`s for I/O

**Reason**: `iostream`s are safe, flexible, extensible.

**Example**:
```cpp
complex<double> z{ 3, 4 };
cout << z << '\n';
```

**Note**: `gets()`, `scanf()` with `%s`, `printf()` with `%s` are security hazards.

**Enforcement**: Optionally flag `<cstdio>` and `<stdio.h>`.

---

### SL.io.10: Unless you use `printf`-family functions call `ios_base::sync_with_stdio(false)`

**Reason**: Synchronizing `iostreams` with `printf` is costly.

**Example**:
```cpp
int main() {
    ios_base::sync_with_stdio(false);
    // use iostreams
}
```

---

### SL.io.50: Avoid `endl`

**Reason**: `endl` slows output by doing redundant `flush()` calls.

**Example**:
```cpp
cout << "Hello, World!" << endl;    // two ops + flush
cout << "Hello, World!\n";          // one op, no flush
```

**Note**: For `cin`/`cout` interaction, flushing is automatic. For files, rarely need flush.

---

### SL.C.1: Don't use setjmp/longjmp

**Reason**: `longjmp` ignores destructors, invalidating RAII resource management.

**Enforcement**: Flag all occurrences of `longjmp` and `setjmp`.

---

## Enum: Enumerations

### Enum.1: Prefer enumerations over macros

**Reason**: Macros don't obey scope and type rules. Names removed during preprocessing.

**Example** (bad):
```cpp
#define RED   0xFF0000
#define GREEN 0x00FF00

int webby = BLUE;   // which BLUE?
```

**Example** (good):
```cpp
enum class Web_color { red = 0xFF0000, green = 0x00FF00, blue = 0x0000FF };
enum class Product_info { red = 0, purple = 1, blue = 2 };

Web_color webby = Web_color::blue;
```

**Enforcement**: Flag macros defining integer values. Use `enum` or `const inline` instead.

---

### Enum.2: Use enumerations to represent sets of related named constants

**Reason**: Shows enumerators are related and can be a named type.

**Example**:
```cpp
enum class Web_color { red = 0xFF0000, green = 0x00FF00, blue = 0x0000FF };
```

**Enforcement**: Flag `switch` where `case`s cover most but not all enumerators.

---

### Enum.3: Prefer class enums over "plain" enums

**Reason**: Traditional enums convert to `int` too readily.

**Example** (bad):
```cpp
enum Web_color { red, green, blue };
Web_color webby = Web_color::blue;
Print_color(webby);  // OK, but maybe unintended
```

**Example** (good):
```cpp
enum class Web_color { red, green, blue };
Print_color(webby);  // Error: cannot convert
```

**Enforcement**: (Simple) Warn on any non-class `enum` definition.

---

### Enum.4: Define operations on enumerations for safe and simple use

**Reason**: Convenience of use and avoidance of errors.

**Example**:
```cpp
enum class Day { mon, tue, wed, thu, fri, sat, sun };

Day& operator++(Day& d) {
    return d = (d == Day::sun) ? Day::mon : static_cast<Day>(static_cast<int>(d)+1);
}
```

**Enforcement**: Flag repeated expressions cast back into an enumeration.

---

### Enum.5: Don't use `ALL_CAPS` for enumerators

**Reason**: Avoid clashes with macros.

**Example** (bad):
```cpp
#define RED   0xFF0000
enum class Product_info { RED, PURPLE, BLUE };   // syntax error
```

**Enforcement**: Flag ALL_CAPS enumerators.

---

### Enum.6: Avoid unnamed enumerations

**Reason**: If you can't name an enumeration, the values are not related.

**Example** (bad):
```cpp
enum { red = 0xFF0000, scale = 4, is_signed = 1 };
```

**Alternative**: Use `constexpr` values instead.

```cpp
constexpr int red = 0xFF0000;
constexpr short scale = 4;
constexpr bool is_signed = true;
```

**Enforcement**: Flag unnamed enumerations.

---

### Enum.7: Specify the underlying type of an enumeration only when necessary

**Reason**: Default (`int`) is easiest to read/write and compatible with C.

**Example**:
```cpp
enum class Direction : char { n, s, e, w, ne, nw, se, sw };  // saves space
enum class Web_color : int32_t { ... };  // redundant
```

**Note**: Specifying underlying type is necessary to forward-declare or ensure specific bit-precision.

---

### Enum.8: Specify enumerator values only when necessary

**Reason**: It's simplest. Avoids duplicate values. Default gives consecutive values good for `switch`.

**Example**:
```cpp
enum class Col1 { red, yellow, blue };  // good
enum class Col2 { red = 1, yellow = 2, blue = 2 }; // typo
enum class Month { jan = 1, feb, mar, ... }; // starting with 1 is conventional
enum class Base_flag { dec = 1, oct = dec << 1, hex = dec << 2 }; // bits
```

**Note**: Specifying values necessary for conventional values (Month) or non-consecutive bits (Base_flag).
