# Memory Safety and Resource Management

Use this reference when checking for memory leaks, resource safety, pointer issues, or const-correctness.

## Quick Checklist
- [ ] No raw new/delete
- [ ] RAII for all resources
- [ ] No dangling pointers
- [ ] Proper const usage

---

## R: Resource Management

### R.1: Manage resources automatically using resource handles and RAII

**Reason**: Avoid leaks and complexity of manual resource management. Encapsulate resources in objects that acquire in constructor and release in destructor.

**Example (bad)**:
```cpp
void send(X* x, string_view destination) {
    auto port = open_port(destination);
    my_mutex.lock();
    // ...
    send(port, x);
    // ...
    my_mutex.unlock();
    close_port(port);
    delete x;  // leaks if exception thrown
}
```

**Example (good)**:
```cpp
void send(unique_ptr<X> x, string_view destination) {
    Port port{destination};            // owns the PortHandle
    lock_guard<mutex> guard{my_mutex}; // owns the lock
    // ...
    send(port, x);
} // automatically unlocks and deletes
```

**Enforcement**: Flag manual resource management without RAII wrappers

---

### R.2: In interfaces, use raw pointers to denote individual objects (only)

**Reason**: Arrays should use containers (vector) or views (span). Raw pointers lose size information.

**Example (bad)**:
```cpp
void f(int* p, int n);   // n is the number of elements - unclear
```

**Example (good)**:
```cpp
void f(int* p, int fmt);   // print *p using format #fmt
void g(gsl::span<int>);    // good, recommended
```

**Enforcement**: Flag pointer arithmetic on non-container pointers

---

### R.3: A raw pointer (a T*) is non-owning

**Reason**: Most raw pointers are non-owning. Owners should be explicitly identified.

**Example**:
```cpp
int* p1 = new int{7};           // bad: raw owning pointer
auto p2 = make_unique<int>(7);  // OK: owned by unique pointer
```

**Example**:
```cpp
template<typename T>
class X {
    T* p;   // bad: unclear ownership
    owner<T*> p;  // OK: explicitly owning
};
```

**Enforcement**: Warn on `delete` of non-owner raw pointer; warn on `new` assigned to raw pointer

---

### R.4: A raw reference (a T&) is non-owning

**Reason**: Raw references are non-owning; ownership should be explicit.

**Example (bad)**:
```cpp
int& r = *new int{7};  // bad: owning reference
delete &r;             // bad
```

**Enforcement**: See R.3

---

### R.5: Prefer scoped objects, don't heap-allocate unnecessarily

**Reason**: Scoped objects (local, global, member) avoid allocation/deallocation overhead.

**Example (bad)**:
```cpp
void f(int n) {
    auto p = new Gadget{n};
    // ...
    delete p;  // could leak
}
```

**Example (good)**:
```cpp
void f(int n) {
    Gadget g{n};  // scoped, automatic cleanup
}
```

**Enforcement**: Warn on objects allocated/deallocated within same function

---

### R.6: Avoid non-const global variables

**See I.2**

---

### R.10: Avoid malloc() and free()

**Reason**: Don't support construction/destruction; don't mix with new/delete.

**Example**:
```cpp
Record* p1 = static_cast<Record*>(malloc(sizeof(Record)));  // *p1 not initialized
auto p2 = new Record;  // default initialized
```

**Enforcement**: Flag explicit use of malloc and free

---

### R.11: Avoid calling new and delete explicitly

**Reason**: Pointer from new should belong to resource handle. Naked new/delete are error-prone.

**Note**: In large programs, naked delete is a likely bug (how do you know you need exactly N deletes?)

**Enforcement**: Warn on explicit new and delete; suggest make_unique

---

### R.12: Immediately give result of explicit resource allocation to manager object

**Reason**: Exception or return might lead to leak otherwise.

**Example (bad)**:
```cpp
void func(const string& name) {
    FILE* f = fopen(name, "r");
    vector<char> buf(1024);  // might fail and leak f
    // ...
}
```

**Example (good)**:
```cpp
void func(const string& name) {
    ifstream f{name};  // RAII handles cleanup
    vector<char> buf(1024);
}
```

**Enforcement**: Flag explicit allocations not given to managers

---

### R.13: Perform at most one explicit resource allocation in single expression statement

**Reason**: Unspecified evaluation order can leak resources.

**Example (bad)**:
```cpp
fun(shared_ptr<Widget>(new Widget(a, b)), shared_ptr<Widget>(new Widget(c, d)));  // potential leak
```

**Example (good)**:
```cpp
fun(make_shared<Widget>(a, b), make_shared<Widget>(c, d));
```

**Enforcement**: Flag expressions with multiple explicit allocations

---

### R.14: Avoid [] parameters, prefer span

**Reason**: Array decays to pointer, losing size information.

**Example**:
```cpp
void f(int[]);           // not recommended
void f(gsl::span<int>);  // good
```

**Enforcement**: Flag [] parameters

---

### R.15: Always overload matched allocation/deallocation pairs

**Reason**: Mismatched operations cause chaos.

**Example**:
```cpp
class X {
    void* operator new(size_t s);
    void operator delete(void*);  // must match
};
```

**Enforcement**: Flag incomplete pairs

---

### R.20: Use unique_ptr or shared_ptr to represent ownership

**Reason**: Prevent resource leaks.

**Example**:
```cpp
X* p1 { new X };              // bad, leaks
auto p2 = make_unique<X>();   // good, unique ownership
auto p3 = make_shared<X>();   // good, shared ownership
```

**Enforcement**: Warn if new result assigned to raw pointer

---

### R.21: Prefer unique_ptr over shared_ptr unless you need to share ownership

**Reason**: unique_ptr is simpler, more predictable (know when destruction happens), and faster (no refcount).

**Example (bad)**:
```cpp
void f() {
    shared_ptr<Base> base = make_shared<Derived>();  // unnecessary refcount
}
```

**Example (good)**:
```cpp
void f() {
    unique_ptr<Base> base = make_unique<Derived>();
}
```

**Enforcement**: Warn if Shared_pointer allocated locally but never returned

---

### R.22: Use make_shared() to make shared_ptrs

**Reason**: More concise; can eliminate separate allocation for refcounts; ensures exception safety.

**Example**:
```cpp
shared_ptr<X> p1 { new X{2} };  // bad
auto p = make_shared<X>(2);     // good
```

**Enforcement**: Warn if shared_ptr constructed from new vs make_shared

---

### R.23: Use make_unique() to make unique_ptrs

**Reason**: More concise; ensures exception safety.

**Example**:
```cpp
unique_ptr<Foo> p {new Foo{7}};  // OK: but repetitive
auto q = make_unique<Foo>(7);    // Better
```

**Enforcement**: Warn if unique_ptr constructed from new vs make_unique

---

### R.24: Use std::weak_ptr to break cycles of shared_ptrs

**Reason**: shared_ptr cycles never reach zero refcount; weak_ptr breaks cycles.

**Example**:
```cpp
class foo {
    shared_ptr<bar> forward_reference_;
};
class bar {
    weak_ptr<foo> back_reference_;  // breaks cycle
};
```

---

### R.30: Take smart pointers as parameters only to explicitly express lifetime semantics

**See F.7**

---

### R.31: If you have non-std smart pointers, follow the basic pattern from std

**Reason**: Rules work for all smart pointers matching the pattern (overload * and ->).

**Note**: Copyable = shared_ptr pattern; non-copyable = unique_ptr pattern

---

### R.32: Take a unique_ptr<widget> parameter to express function assumes ownership

**Reason**: Documents and enforces ownership transfer.

**Example**:
```cpp
void sink(unique_ptr<widget>);  // takes ownership
void uses(widget*);             // just uses widget
```

**Enforcement**: Warn if Unique_ptr& param doesn't assign or call reset()

---

### R.33: Take a unique_ptr<widget>& parameter to express function reseats widget

**Reason**: Documents and enforces reseating semantics. "Reseat" = make pointer refer to different object.

**Example**:
```cpp
void reseat(unique_ptr<widget>&);  // will/might reseat
```

**Enforcement**: Warn if Unique_ptr& param doesn't assign or call reset()

---

### R.34: Take a shared_ptr<widget> parameter to express shared ownership

**Reason**: Makes ownership sharing explicit.

**Example**:
```cpp
class WidgetUser {
    explicit WidgetUser(shared_ptr<widget> w) noexcept : m_widget{std::move(w)} {}
    // ...
};
```

**Enforcement**: Warn if Shared_pointer& doesn't assign/reset; warn if Shared_pointer by value/const& doesn't copy/move

---

### R.35: Take a shared_ptr<widget>& parameter to express function might reseat shared pointer

**Reason**: Makes reseating explicit.

**Example**:
```cpp
void ChangeWidget(shared_ptr<widget>& w) {
    w = std::make_shared<widget>(widget{});
}
```

**Enforcement**: Warn if Shared_pointer& doesn't assign/reset; warn if by value/const& doesn't copy/move

---

### R.36: Take a const shared_ptr<widget>& parameter to express it might retain refcount

**Reason**: Makes potential refcount retention explicit.

**Example**:
```cpp
void share(shared_ptr<widget>);            // will retain refcount
void reseat(shared_ptr<widget>&);          // might reseat
void may_share(const shared_ptr<widget>&); // might retain refcount
```

---

### R.37: Do not pass pointer/reference obtained from aliased smart pointer

**Reason**: Number one cause of dangling pointers. Smart pointer might be reset/deleted during call chain.

**Example (bad)**:
```cpp
shared_ptr<widget> g_p = ...;
void my_code() {
    f(*g_p);  // BAD: g_p could be reset in f/callees
}
```

**Example (good)**:
```cpp
void my_code() {
    auto pin = g_p;  // keep refcount for call tree
    f(*pin);         // GOOD
}
```

**Enforcement**: Warn if pointer/reference from non-local or aliased smart pointer used in call

---

## ES: Memory & Pointer Safety

### ES.42: Keep use of pointers simple and straightforward

**Reason**: Complicated pointer manipulation is major error source.

**Example (bad)**:
```cpp
void f(int* p, int count) {
    int* q = p + 1;    // BAD
    int n = *p++;      // BAD
    p[4] = 1;          // BAD
}
```

**Example (good)**:
```cpp
void f(span<int> a) {  // BETTER
    int n = a[0];
    span<int> q = a.subspan(1);
    a[4] = 1;
}
```

**Enforcement**: Flag pointer arithmetic; flag array indexing with non-constant

---

### ES.43: Avoid expressions with undefined order of evaluation

**Reason**: You have no idea what such code does; portability issues.

**Example (bad)**:
```cpp
v[i] = ++i;  // undefined
```

**Rule of thumb**: Don't read value twice in expression where you write to it.

---

### ES.44: Don't depend on order of evaluation of function arguments

**Reason**: Order is unspecified.

**Example (bad)**:
```cpp
int i = 0;
f(++i, ++i);  // unspecified: f(1,2) or f(2,1)
```

---

### ES.45: Avoid "magic constants"; use symbolic constants

**Reason**: Unnamed constants easily overlooked; hard to understand.

**Example (bad)**:
```cpp
for (int m = 1; m <= 12; ++m)  // don't: magic constant 12
```

**Example (good)**:
```cpp
constexpr int first_month = 1;
constexpr int last_month = 12;
for (int m = first_month; m <= last_month; ++m)
```

**Enforcement**: Flag literals (except 0, 1, nullptr, \n, "")

---

### ES.46: Avoid lossy (narrowing, truncating) arithmetic conversions

**Reason**: Narrowing destroys information, often unexpectedly.

**Example (bad)**:
```cpp
double d = 7.9;
int i = d;    // bad: narrowing

void f(int x) {
    char c1 = x;   // bad: narrowing
}
```

**Example (good)**:
```cpp
i = gsl::narrow_cast<int>(d);   // OK (you asked for it)
i = gsl::narrow<int>(d);        // OK: throws narrowing_error
```

---

### ES.47: Use nullptr rather than 0 or NULL

**Reason**: Readability; nullptr can't be confused with int; well-specified type.

**Example**:
```cpp
void f(int);
void f(char*);
f(0);         // call f(int)
f(nullptr);   // call f(char*)
```

**Enforcement**: Flag uses of 0 and NULL for pointers

---

### ES.48: Avoid casts

**Reason**: Well-known source of errors; make optimizations unreliable.

**Example (bad)**:
```cpp
double d = 2;
auto p = (long*)&d;  // undefined behavior
```

**Enforcement**: Flag all C-style casts; flag functional style casts

---

### ES.49: If you must use cast, use a named cast

**Reason**: Readability; error avoidance; more specific.

**Named casts**: static_cast, const_cast, reinterpret_cast, dynamic_cast, std::move, std::forward, gsl::narrow_cast, gsl::narrow

**Example**:
```cpp
D* pd2 = static_cast<D*>(pb);       // error: D not derived from B
D* pd3 = reinterpret_cast<D*>(pb);  // OK: on your head be it
D* pd4 = dynamic_cast<D*>(pb);      // OK: return nullptr
```

---

### ES.50: Don't cast away const

**Reason**: Makes lie out of const; modifying const is undefined behavior.

**Example (bad)**:
```cpp
void f(const int& x) {
    const_cast<int&>(x) = 42;   // BAD
}
```

**Enforcement**: Flag const_cast

---

### ES.51: Avoid variety of different length types

**Reason**: Mix of types of different sizes can confuse; use span/pSpan instead.

(Refer to full guidelines for details)

---

### ES.55: Avoid the need for range checking

**Reason**: Constructs that cannot overflow don't overflow (and run faster).

**Example**:
```cpp
for (auto& x : v)      // no range checking needed
    cout << x << '\n';
```

---

### ES.56: Write std::move() only when you need to explicitly move object to another scope

**Reason**: Move leaves behind empty object; implicit move happens for rvalues.

**Example (bad)**:
```cpp
vector<int> make_vector() {
    vector<int> result;
    return std::move(result);  // bad; just "return result;"
}
```

**Example (good)**:
```cpp
void sink(unique_ptr<widget> p);
auto w = make_unique<widget>();
sink(std::move(w));  // ok, give to sink()
```

---

### ES.60: Avoid new and delete outside resource management functions

**Reason**: Direct resource management in application code is error-prone.

**Example (bad)**:
```cpp
void f(int n) {
    auto p = new X[n];
    // ...
    delete[] p;
}
```

**Enforcement**: Flag naked new and delete

---

### ES.61: Delete arrays using delete[] and non-arrays using delete

**Reason**: Mismatches lead to resource release errors/memory corruption.

**Example (bad)**:
```cpp
auto p = new X[n];
delete p;  // error: should be delete[]
```

---

### ES.62: Don't compare pointers into different arrays

**Reason**: Result is undefined.

**Example (bad)**:
```cpp
int a1[7];
int a2[9];
if (&a1[5] < &a2[7]) {}  // bad: undefined
```

---

### ES.63: Don't slice

**Reason**: Slicing copies only part of object, usually leads to errors.

**Example**:
```cpp
class Shape { /* ... */ };
class Circle : public Shape { /* ... */ };
Circle c { {0, 0}, 42 };
Shape s {c};  // copy construct only Shape part
```

**Enforcement**: Warn against slicing

---

### ES.64: Use the T{e} notation for construction

**Reason**: Makes construction explicit; doesn't allow narrowing; safe and general.

**Example**:
```cpp
int x2 = int{d};     // error: narrowing - use cast if needed
int y2 = int(d);     // bad: narrowing
int z2 = (int)d;     // bad: narrowing
```

---

### ES.65: Don't dereference an invalid pointer

**Reason**: Undefined behavior; crashes, wrong results, memory corruption.

**Example (bad)**:
```cpp
void f() {
    int x = 0;
    int* p = &x;
    if (condition()) {
        int y = 0;
        p = &y;
    }  // invalidates p
    *p = 42;  // BAD, p might be invalid
}
```

---

### ES.100: Don't mix signed and unsigned arithmetic

**Reason**: Avoid wrong results.

**Example**:
```cpp
int x = -3;
unsigned int y = 7;
cout << x - y << '\n';  // unsigned result, possibly 4294967286
```

---

### ES.101: Use unsigned types for bit manipulation

**Reason**: Support bit manipulation without surprises from sign bits.

**Example**:
```cpp
unsigned char x = 0b1010'1010;
unsigned char y = ~x;   // y == 0b0101'0101
```

---

### ES.102: Use signed types for arithmetic

**Reason**: Most arithmetic assumed signed; x-y yields negative when y>x.

**Example (bad)**:
```cpp
unsigned int u = 5;
cout << subtract(u, 7) << '\n';  // 4294967294
```

---

### ES.103: Don't overflow

**Reason**: Makes numeric algorithm meaningless; memory corruption.

**Example (bad)**:
```cpp
int a[10];
a[10] = 7;  // bad, array bounds overflow
int n = numeric_limits<int>::max();
int m = n + 1;  // bad, numeric overflow
```

---

### ES.104: Don't underflow

**Reason**: Memory corruption; undefined behavior.

**Example (bad)**:
```cpp
int a[10];
a[-2] = 7;  // bad
```

---

### ES.105: Don't divide by integer zero

**Reason**: Undefined; probably crash.

**Example (good)**:
```cpp
int divide(int a, int b) {
    Expects(b != 0);
    return a / b;
}
```

---

### ES.106: Don't try to avoid negative values by using unsigned

**Reason**: Doesn't eliminate negative values; causes signed/unsigned mix errors.

**Example**:
```cpp
unsigned int u1 = -2;  // Valid: value is 4294967294
unsigned area(unsigned height, unsigned width) {
    return height*width;  // if input is -2, becomes 4294967292
}
```

---

### ES.107: Don't use unsigned for subscripts, prefer gsl::index

**Reason**: Avoid signed/unsigned confusion; better optimization; better error detection.

**Example (bad)**:
```cpp
for (int i = 0; i < vec.size(); i++)  // might not be big enough
for (auto i = vec.size()-1; i >= 0; i--)  // bug
```

**Example (good)**:
```cpp
for (gsl::index i = 0; i < vec.size(); i++)  // ok
for (gsl::index i = vec.size()-1; i >= 0; i--)  // ok
```

---

## Con: Constants & Immutability

You can't have a race condition on a constant. It is easier to reason about a program when many of the objects cannot change their values.

### Con.1: By default, make objects immutable

**Reason**: Immutable objects are easier to reason about, so make objects non-`const` only when there is a need to change their value. Prevents accidental or hard-to-notice change of value.

**Example**:
```cpp
for (const int i : c) cout << i << '\n';    // just reading: const
for (int i : c) cout << i << '\n';          // BAD: just reading
```

**Enforcement**: Flag non-`const` variables that are not modified (except for parameters and returned local variables to avoid false positives).

---

### Con.2: By default, make member functions `const`

**Reason**: A member function should be marked `const` unless it changes the object's observable state. This gives a more precise statement of design intent, better readability, more errors caught by the compiler, and sometimes more optimization opportunities.

**Example, bad**:
```cpp
class Point {
    int x, y;
public:
    int getx() { return x; }    // BAD, should be const
};
```

**Enforcement**: Flag a member function that is not marked `const`, but that does not perform a non-`const` operation on any data member.

---

### Con.3: By default, pass pointers and references to `const`s

**Reason**: To avoid a called function unexpectedly changing the value. It's far easier to reason about programs when called functions don't modify state.

**Example**:
```cpp
void f(char* p);        // does f modify *p? (assume it does)
void g(const char* p);  // g does not modify *p
```

**Enforcement**:
* Flag a function that does not modify an object passed by pointer or reference to non-`const`
* Flag a function that (using a cast) modifies an object passed by pointer or reference to `const`

---

### Con.4: Use `const` to define objects with values that do not change after construction

**Reason**: Prevent surprises from unexpectedly changed object values.

**Example**:
```cpp
void f()
{
    int x = 7;
    const int y = 9;
    for (;;) {
        // ...
    }
    // As x is not const, we must assume that it is modified somewhere in the loop.
}
```

**Enforcement**: Flag unmodified non-`const` variables.

---

### Con.5: Use `constexpr` for values that can be computed at compile time

**Reason**: Better performance, better compile-time checking, guaranteed compile-time evaluation, no possibility of race conditions.

**Example**:
```cpp
double x = f(2);            // possible run-time evaluation
const double y = f(2);      // possible run-time evaluation
constexpr double z = f(2);  // error unless f(2) can be evaluated at compile time
```

**Enforcement**: Flag `const` definitions with constant expression initializers.
