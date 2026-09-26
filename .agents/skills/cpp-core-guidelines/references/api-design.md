# API and Interface Design

Use this reference when designing function signatures, public APIs, or reviewing interface clarity.

## Quick Checklist
- [ ] Interfaces are explicit
- [ ] Parameters express intent/ownership
- [ ] Return values preferred over output params
- [ ] No global state in interfaces

---

## I: Interfaces

### I.1: Make interfaces explicit

**Reason:** Correctness. Assumptions not stated in an interface are easily overlooked and hard to test.

**Bad Example:**
```cpp
int round(double d) {
    return (round_up) ? ceil(d) : d;  // don't: "invisible" dependency
}
```

**Enforcement:**
- A function should not make control-flow decisions based on namespace scope variables
- A function should not write to namespace scope variables

---

### I.2: Avoid non-`const` global variables

**Reason:** Non-`const` global variables hide dependencies and make dependencies subject to unpredictable changes.

**Example:**
```cpp
struct Data { /* ... */ } data;  // non-const data
void compute() { /* ... use data ... */ }
```

**Enforcement:** Report all non-`const` variables declared at namespace scope.

---

### I.3: Avoid singletons

**Reason:** Singletons are basically complicated global objects in disguise.

**Exception:** You can use the simplest "singleton" for initialization on first use:
```cpp
X& myX() {
    static X my_x {3};
    return my_x;
}
```

**Enforcement:**
- Look for classes with names including "singleton"
- Look for classes where only a single object is created

---

### I.4: Make interfaces precisely and strongly typed

**Reason:** Types are the simplest and best documentation, improve legibility, and are checked at compile time.

**Bad Example:**
```cpp
void pass(void* data);  // weak type
draw_rect(100, 200, 100, 500);  // what do numbers specify?
```

**Good Example:**
```cpp
void draw_rectangle(Point top_left, Point bottom_right);
void blink_led(milliseconds time_to_blink);  // unit explicit
```

**Enforcement:**
- Report use of `void*` as parameter or return type
- Report use of more than one `bool` parameter

---

### I.5: State preconditions (if any)

**Reason:** Arguments have meaning that might constrain their proper use.

**Example:**
```cpp
double sqrt(double x);  // x must be non-negative
double sqrt(double x) { Expects(x >= 0); /* ... */ }
```

---

### I.6: Prefer `Expects()` for expressing preconditions

**Reason:** To make it clear that the condition is a precondition and to enable tool use.

**Example:**
```cpp
int area(int height, int width) {
    Expects(height > 0 && width > 0);  // good
    if (height <= 0 || width <= 0) my_error();  // obscure
}
```

---

### I.7: State postconditions

**Reason:** To detect misunderstandings about the result and possibly catch erroneous implementations.

**Bad Example:**
```cpp
int area(int height, int width) { return height * width; }  // bad: overflow possible
```

**Good Example:**
```cpp
int area(int height, int width) {
    auto res = height * width;
    Ensures(res > 0);
    return res;
}
```

---

### I.8: Prefer `Ensures()` for expressing postconditions

**Reason:** To make it clear that the condition is a postcondition and to enable tool use.

**Example:**
```cpp
void f() {
    char buffer[MAX];
    memset(buffer, 0, MAX);
    Ensures(buffer[0] == 0);
}
```

---

### I.9: If an interface is a template, document its parameters using concepts

**Reason:** Make the interface precisely specified and compile-time checkable.

**Example:**
```cpp
template<typename Iter, typename Val>
  requires input_iterator<Iter> && equality_comparable_with<iter_value_t<Iter>, Val>
Iter find(Iter first, Iter last, Val v) {
    // ...
}
```

**Enforcement:** Warn if any non-variadic template parameter is not constrained by a concept.

---

### I.10: Use exceptions to signal a failure to perform a required task

**Reason:** It should not be possible to ignore an error because that could leave the system in an undefined state.

**Example:**
```cpp
int printf(const char* ...);  // bad: returns negative on failure
template<class F, class ...Args>
explicit thread(F&& f, Args&&... args);  // good: throws on failure
```

---

### I.11: Never transfer ownership by a raw pointer (`T*`) or reference (`T&`)

**Reason:** If there is doubt whether caller or callee owns an object, leaks or premature destruction will occur.

**Bad Example:**
```cpp
X* compute(args) {  // don't
    X* res = new X{};
    return res;
}
```

**Good Example:**
```cpp
vector<double> compute(args) {  // good
    vector<double> res(10000);
    return res;
}
```

**Alternative:**
```cpp
owner<X*> compute(args);  // mark ownership explicitly
```

**Enforcement:**
- Warn on `delete` of raw pointer that is not `owner<T>`
- Warn if return value of `new` assigned to raw pointer

---

### I.12: Declare a pointer that must not be null as `not_null`

**Reason:** To help avoid dereferencing `nullptr` errors. To improve performance by avoiding redundant checks.

**Example:**
```cpp
int length(const char* p);  // unclear if nullptr valid
int length(not_null<const char*> p);  // better: p cannot be nullptr
```

**Enforcement:** If function checks pointer against `nullptr` on all paths, warn it should be `not_null`.

---

### I.13: Do not pass an array as a single pointer

**Reason:** (pointer, size)-style interfaces are error-prone. A plain pointer must rely on convention to determine size.

**Bad Example:**
```cpp
void copy_n(const T* p, T* q, int n);  // error-prone
void draw(Shape* p, int n);  // poor interface
Circle arr[10];
draw(arr, 10);
```

**Good Example:**
```cpp
void copy(span<const T> r, span<T> r2);
void draw2(span<Circle>);
draw2(arr);  // deduce size
```

**Enforcement:**
- Warn for implicit array-to-pointer conversion
- Warn for arithmetic on pointer types

---

### I.22: Avoid complex initialization of global objects

**Reason:** Complex initialization can lead to undefined order of execution.

**Example:**
```cpp
// file1.c
extern const X x;
const Y y = f(x);  // read x; write y

// file2.c
extern const Y y;
const X x = g(y);  // read y; write x
// Order of f() and g() is undefined
```

**Enforcement:**
- Flag initializers of globals calling non-`constexpr` functions
- Flag initializers accessing `extern` objects

---

### I.23: Keep the number of function arguments low

**Reason:** Having many arguments opens opportunities for confusion.

**Discussion:** Common reasons for too many parameters:
1. Missing an abstraction - compound value passed as individual elements
2. Violating "one function, one responsibility"

**Example:**
```cpp
template<class In1, class In2, class Out>
  requires mergeable<In1, In2, Out>
Out merge(In1 r1, In2 r2, Out result);  // bundled arguments
```

**Enforcement:** Warn when function declares two iterators instead of a range

---

### I.24: Avoid adjacent parameters that can be invoked by the same arguments in either order

**Reason:** Adjacent arguments of the same type are easily swapped by mistake.

**Bad Example:**
```cpp
void copy_n(T* p, T* q, int n);  // easy to reverse p and q
```

**Good Example:**
```cpp
void copy_n(const T* p, T* q, int n);  // const for "from"
void copy_n(span<const T> p, span<T> q);  // use spans
```

**Enforcement:** Warn if two consecutive parameters share the same type.

---

### I.25: Prefer empty abstract classes as interfaces to class hierarchies

**Reason:** Abstract classes that are empty are more likely to be stable than base classes with state.

**Bad Example:**
```cpp
class Shape {  // bad: interface with data
public:
    Point center() const { return c; }
    virtual void draw() const;
private:
    Point c;
    vector<Point> outline;
    Color col;
};
```

**Good Example:**
```cpp
class Shape {  // better: pure interface
public:
    virtual Point center() const = 0;
    virtual void draw() const = 0;
    virtual void rotate(int) = 0;
    virtual ~Shape() = default;
};
```

**Enforcement:** Warn if pointer/reference to class assigned to base and base contains data members.

---

### I.26: If you want a cross-compiler ABI, use a C-style subset

**Reason:** Different compilers implement different binary layouts for classes, exception handling, and function names.

---

### I.27: For stable library ABI, consider the Pimpl idiom

**Reason:** Changes to implementation details require recompilation. Pimpl can isolate users from changes.

**Example:**
```cpp
// widget.h
class widget {
    class impl;
    std::unique_ptr<impl> pimpl;
public:
    void draw();
    widget(int);
    ~widget();
    // ...
};

// widget.cpp
class widget::impl {
    int n;
public:
    void draw(const widget& w) { /* ... */ }
    impl(int n) : n(n) {}
};
```

---

### I.30: Encapsulate rule violations

**Reason:** To keep code simple and safe. Sometimes ugly/unsafe techniques are necessary - keep them local.

**Example:**
```cpp
class Istream {
public:
    enum Opt { from_line = 1 };
    Istream() { }
    Istream(czstring p) : owned{true}, inp{new ifstream{p}} {}
    Istream(czstring p, Opt) : owned{true}, inp{new istringstream{p}} {}
    ~Istream() { if (owned) delete inp; }
    operator istream&() { return *inp; }
private:
    bool owned = false;
    istream* inp = &cin;
};
```

**Enforcement:** Flag rule suppression that enable violations to cross interfaces

---

## F: Functions

### F.1: "Package" meaningful operations as carefully named functions

**Reason:** Factoring out common code makes code more readable, more likely to be reused, and limit errors from complex code.

**Example - Bad:**
```cpp
void read_and_print(istream& is) {
    int x;
    if (is >> x)
        cout << "the int is " << x << '\n';
    else
        cerr << "no int on input\n";
}
```

**Example - Good:**
```cpp
auto lessT = [](T x, T y) { return x.rank() < y.rank() && x.value() < y.value(); };
sort(a, b, lessT);
```

**Enforcement:** See F.3; Flag identical and very similar lambdas used in different places.

---

### F.2: A function should perform a single logical operation

**Reason:** A function that performs a single operation is simpler to understand, test, and reuse.

**Example - Bad:**
```cpp
void read_and_print() {
    int x;
    cin >> x;
    cout << x << "\n";
}
```

**Example - Good:**
```cpp
int read(istream& is) { int x; is >> x; return x; }
void print(ostream& os, int x) { os << x << "\n"; }
```

**Enforcement:**
* Consider functions with more than one "out" parameter suspicious
* Consider "large" functions that don't fit on one editor screen suspicious
* Consider functions with 7+ parameters suspicious

---

### F.3: Keep functions short and simple

**Reason:** Large functions are hard to read, more likely to contain complex code, and more likely to have variables in larger than minimal scopes.

**Example - Good:** Break into smaller cohesive functions:
```cpp
double func1_muon(double val, int flag) { /* ... */ }
double func1_tau(double val, int flag1, int flag2) { /* ... */ }
```

**Enforcement:**
* Flag functions that don't fit on a screen (~60 lines x 140 chars)
* Flag functions with more than 10 logical paths

---

### F.4: If a function might have to be evaluated at compile time, declare it `constexpr`

**Reason:** `constexpr` is needed to tell the compiler to allow compile-time evaluation.

**Example:**
```cpp
constexpr int fac(int n) {
    constexpr int max_exp = 17;
    Expects(0 <= n && n < max_exp);
    int x = 1;
    for (int i = 2; i <= n; ++i) x *= i;
    return x;
}
```

---

### F.5: If a function is very small and time-critical, declare it `inline`

**Reason:** Some optimizers are good at inlining without hints, but don't rely on it. Specifying inline encourages the compiler.

**Example:**
```cpp
inline string cat(const string& s, const string& s2) { return s + s2; }
```

---

### F.6: If your function must not throw, declare it `noexcept`

**Reason:** Helps optimizers by reducing execution paths. Speeds up exit after failure.

**Example:**
```cpp
vector<string> collect(istream& is) noexcept {
    vector<string> res;
    for (string s; is >> s;) res.push_back(s);
    return res;
}
```

**Enforcement:**
* Flag low-level functions that are not `noexcept`, yet cannot throw
* Flag throwing `swap`, `move`, destructors, and default constructors

---

### F.7: For general use, take `T*` or `T&` arguments rather than smart pointers

**Reason:** Passing a smart pointer transfers or shares ownership. A function that doesn't manipulate lifetime should take raw pointers or references.

**Example - Bad:**
```cpp
void f(shared_ptr<widget>& w) { use(*w); }  // only uses w
f(stack_widget);  // error
```

**Example - Good:**
```cpp
void f(widget& w) { use(w); }
f(stack_widget);  // ok
```

**Enforcement:**
* Warn if function takes smart pointer but only calls `operator*`, `operator->` or `get()`
* Flag smart pointer parameters never copied/moved from

---

### F.8: Prefer pure functions

**Reason:** Pure functions are easier to reason about, sometimes easier to optimize and parallelize.

**Example:**
```cpp
template<class T>
auto square(T t) { return t * t; }
```

---

### F.9: Unused parameters should be unnamed

**Reason:** Readability. Suppresses unused parameter warnings.

**Example:**
```cpp
widget* find(const set<widget>& s, const widget& w, Hint);
```

Or with attribute:
```cpp
Value* find(const set<Value>& s, const Value& v, [[maybe_unused]] Hint h);
```

**Enforcement:** Flag named unused parameters.

---

### F.10: If an operation can be reused, give it a name

**Reason:** Documentation, readability, opportunity for reuse.

**Example:**
```cpp
bool compare_insensitive(const string& a, const string& b) {
    if (a.size() != b.size()) return false;
    for (int i = 0; i < a.size(); ++i)
        if (tolower(a[i]) != tolower(b[i])) return false;
    return true;
}
```

**Enforcement:** Flag similar lambdas.

---

### F.11: Use an unnamed lambda if you need a simple function object in one place only

**Reason:** Makes code concise and gives better locality.

**Example:**
```cpp
auto earlyUsersEnd = std::remove_if(users.begin(), users.end(),
    [](const User &a) { return a.id > 100; });
```

**Enforcement:** Look for identical and near identical lambdas.

---

### F.15: Prefer simple and conventional ways of passing information

**Reason:** Using "unusual and clever" techniques causes surprises and encourages bugs.

---

### F.16: For "in" parameters, pass cheaply-copied types by value and others by reference to `const`

**Reason:** Both let caller know function won't modify argument. For small types (2-3 words), pass by value.

**Example:**
```cpp
void f1(const string& s);  // OK: pass by reference to const
void f2(string s);         // bad: potentially expensive
void f3(int x);            // OK: pass by value
void f4(const int& x);     // bad: overhead
```

**Enforcement:**
* Warn when value parameter size > 2 * sizeof(void*)
* Warn when const& parameter size <= 2 * sizeof(void*)

---

### F.17: For "in-out" parameters, pass by reference to non-`const`

**Reason:** Makes it clear to callers that the object is assumed to be modified.

**Example:**
```cpp
void update(Record& r);  // assume update writes to r
```

**Enforcement:**
* Warn about functions with non-const reference parameters that don't write to them
* Warn when non-const reference parameter is moved

---

### F.18: For "will-move-from" parameters, pass by `X&&` and `std::move` the parameter

**Reason:** Efficient and eliminates bugs at call site.

**Example:**
```cpp
void sink(vector<int>&& v) {
    store_somewhere(std::move(v));
}
```

**Exception:** Unique owner types like `unique_ptr` can be passed by value (simpler).

**Enforcement:**
* Flag `X&&` parameters used without `std::move`
* Flag access to moved-from objects

---

### F.19: For "forward" parameters, pass by `TP&&` and only `std::forward` the parameter

**Reason:** When passing onward to other code, make function agnostic to const-ness and rvalue-ness.

**Example:**
```cpp
template<class F, class... Args>
inline decltype(auto) invoke(F&& f, Args&&... args) {
    return forward<F>(f)(forward<Args>(args)...);
}
```

**Enforcement:** Flag `TP&&` parameter that does anything other than `std::forward` exactly once.

---

### F.20: For "out" output values, prefer return values to output parameters

**Reason:** A return value is self-documenting, whereas `&` could be in-out or out-only.

**Example:**
```cpp
vector<const int*> find_all(const vector<int>&, int x);  // OK
void find_all(const vector<int>&, vector<const int*>& out, int x);  // Bad
```

**Enforcement:** Flag reference to non-const parameters not read before being written.

---

### F.21: To return multiple "out" values, prefer returning a struct

**Reason:** Self-documenting as "output-only". Use named struct if possible.

**Example - Bad:**
```cpp
int f(const string& input, /*output only*/ string& output_data) {
    output_data = something();
    return status;
}
```

**Example - Good:**
```cpp
struct f_result { int status; string data; };
f_result f(const string& input) {
    return {status, something()};
}
```

**Enforcement:**
* Output parameters should be replaced by return values
* `pair`/`tuple` return types should be replaced by `struct`

---

### F.22: Use `T*` or `owner<T*>` to designate a single object

**Reason:** Readability - makes meaning of plain pointer clear.

**Example - Bad:**
```cpp
void use(int* p, int n, char* s, int* q) {
    delete q;  // Bad: don't know if *q is allocated
}
```

**Example - Good:**
```cpp
void use2(span<int> p, zstring s, owner<int*> q) {
    delete q;  // OK
}
```

**Enforcement:** Warn for arithmetic operations on pointer expressions.

---

### F.23: Use a `not_null<T>` to indicate that "null" is not a valid value

**Reason:** Clarity. Makes it clear that caller is responsible for nullptr checks.

**Example:**
```cpp
int length(not_null<Record*> p);  // caller ensures p != nullptr
int length(Record* p);             // implementor must assume p == nullptr possible
```

**Enforcement:**
* Warn if raw pointer dereferenced without test
* Warn if not_null pointer tested against nullptr

---

### F.24: Use a `span<T>` or a `span_p<T>` to designate a half-open sequence

**Reason:** Informal/non-explicit ranges are a source of errors.

**Example:**
```cpp
X* find(span<X> r, const X& v);
vector<X> vec;
auto p = find({vec.begin(), vec.end()}, X{});
```

**Enforcement:** Warn where accesses to pointer parameters bounded by integral parameters - suggest using span.

---

### F.25: Use a `zstring` or a `not_null<zstring>` to designate a C-style string

**Reason:** Must distinguish C-style strings from pointer to single character.

**Example:**
```cpp
int length(zstring p);  // implementor assumes p == nullptr possible
int length(not_null<zstring> p);  // caller ensures p != nullptr
```

---

### F.26: Use a `unique_ptr<T>` to transfer ownership where a pointer is needed

**Reason:** Cheapest way to pass a pointer safely.

**Example:**
```cpp
unique_ptr<Shape> get_shape(istream& is) {
    auto kind = read_header(is);
    switch (kind) {
        case kCircle: return make_unique<Circle>(is);
        case kTriangle: return make_unique<Triangle>(is);
    }
}
```

**Enforcement:** Warn if function returns locally allocated raw pointer.

---

### F.27: Use a `shared_ptr<T>` to share ownership

**Reason:** Standard way to represent shared ownership.

**Example:**
```cpp
shared_ptr<const Image> im { read_image(somewhere) };
std::thread t0 {shade, args0, top_left, im};
```

---

### F.42: Return a `T*` to indicate a position (only)

**Reason:** That's what pointers are good for. Returning `T*` to transfer ownership is misuse.

**Example:**
```cpp
Node* find(Node* t, const string& s) {
    if (!t || t->name == s) return t;
    if ((auto p = find(t->left, s))) return p;
    if ((auto p = find(t->right, s))) return p;
    return nullptr;
}
```

**Enforcement:**
* Flag `delete`, `std::free()` on plain `T*`
* Flag `new`, `malloc()` assigned to plain `T*`

---

### F.43: Never (directly or indirectly) return a pointer or a reference to a local object

**Reason:** To avoid crashes and data corruption from dangling pointers.

**Example - Bad:**
```cpp
int* f() {
    int fx = 9;
    return &fx;  // BAD
}
```

**Enforcement:** Compilers catch return of reference to locals; static analysis catches many patterns.

---

### F.44: Return a `T&` when copy is undesirable and "returning no object" isn't needed

**Reason:** Language guarantees `T&` refers to an object - no nullptr test needed.

**Example:**
```cpp
class Car {
    array<wheel, 4> w;
public:
    wheel& get_wheel(int i) { return w[i]; }
};
```

**Enforcement:** Flag functions where no return expression could yield nullptr.

---

### F.45: Don't return a `T&&`

**Reason:** Asking to return reference to destroyed temporary object.

**Example - Bad:**
```cpp
template<class F>
auto&& wrapper(F f) {
    return f();  // BAD: returns reference to temporary
}
```

**Example - Good:**
```cpp
template<class F>
auto wrapper(F f) {
    return f();  // OK
}
```

**Enforcement:** Flag any `&&` return type except in `std::move` and `std::forward`.

---

### F.46: `int` is the return type for `main()`

**Reason:** Language rule - `void main()` limits portability.

**Example - Bad:**
```cpp
void main() { /* ... */ }
```

**Example - Good:**
```cpp
int main() { std::cout << "This is the way\n"; }
```

---

### F.47: Return `T&` from assignment operators

**Reason:** Convention ensures consistency with standard-library types.

**Example:**
```cpp
class Foo {
public:
    Foo& operator=(const Foo& rhs) {
        // Copy members
        return *this;
    }
};
```

**Enforcement:** Check return type of assignment operators.

---

### F.48: Don't `return std::move(local)`

**Reason:** Returning local variable implicitly moves. Explicit `std::move` prevents RVO.

**Example - Bad:**
```cpp
S bad() { S result; return std::move(result); }
```

**Example - Good:**
```cpp
S good() { S result; return result; }
```

**Enforcement:** Check return expressions for `std::move(local)`.

---

### F.49: Don't return `const T`

**Reason:** Obsolete advice - interferes with move semantics.

**Example - Bad:**
```cpp
const vector<int> fct();  // bad: prevents move semantics
```

**Enforcement:** Flag returning const value.

---

### F.50: Use a lambda when a function won't do (to capture local variables, or to write a local function)

**Reason:** Functions can't capture or be at local scope; lambdas can. Functions can overload.

**Example:**
```cpp
void f(int);
void f(const string&);

vector<work> v = lots_of_work();
for (int tasknum = 0; tasknum < max; ++tasknum) {
    pool.run([=, &v] { /* process chunk of v */ });
}
```

**Enforcement:** Warn on named non-generic lambda capturing nothing at global scope.

---

### F.51: Where there is a choice, prefer default arguments over overloading

**Reason:** Default arguments provide alternative interfaces to single implementation.

**Example:**
```cpp
void print(const string& s, format f = {});  // preferred

// vs
void print(const string& s);
void print(const string& s, format f);  // less preferred
```

**Enforcement:** Warn on overload sets with common prefix of parameters.

---

### F.52: Prefer capturing by reference in lambdas that will be used locally

**Reason:** For efficiency and correctness when using lambda locally.

**Example:**
```cpp
std::for_each(begin(sockets), end(sockets), [&message](auto& socket) {
    socket.send(message);
});
```

**Enforcement:** Flag lambda capturing by reference but used non-locally.

---

### F.53: Avoid capturing by reference in lambdas that will be used non-locally

**Reason:** Pointers and references to locals shouldn't outlive scope.

**Example - Bad:**
```cpp
int local = 42;
thread_pool.queue_work([&] { process(local); });  // UB!
```

**Example - Good:**
```cpp
int local = 42;
thread_pool.queue_work([=] { process(local); });
```

**Enforcement:**
* Warn when capture-list contains reference to locally declared variable
* Flag when such lambda passed to non-local context

---

### F.54: When writing a lambda that captures `this`, don't use `[=]` default capture

**Reason:** Confusing - `[=]` appears to capture by value, but captures `this` pointer by value.

**Example - Bad:**
```cpp
auto lambda = [=] { use(i, x); };  // BAD: x captured by reference via this
```

**Example - Good:**
```cpp
auto lambda2 = [i, this] { use(i, x); };
```

**Enforcement:** Flag lambda with `[=]` that captures `this`.

---

### F.55: Don't use `va_arg` arguments

**Reason:** Fragile - cannot be enforced safe by language.

**Example - Bad:**
```cpp
int sum(...) {
    while (/*...*/)
        result += va_arg(list, int);  // BAD
}
```

**Example - Good:**
```cpp
template<class ...Args>
auto sum(Args... args) {
    return (... + args);
}
```

**Enforcement:** Issue diagnostic for using `va_list`, `va_start`, `va_arg`.

---

### F.56: Avoid unnecessary condition nesting

**Reason:** Shallow nesting makes code easier to follow.

**Example - Bad:**
```cpp
void foo() {
    if (x) {
        computeImportantThings(x);
    }
}
```

**Example - Good:**
```cpp
void foo() {
    if (!x) return;
    computeImportantThings(x);
}
```

**Enforcement:** Flag redundant `else`. Flag function body that is just conditional enclosing block.
