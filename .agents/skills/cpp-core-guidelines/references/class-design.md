# Class Design

Use this reference when designing classes, reviewing OOP structure, or checking constructor/destructor patterns.

## Quick Checklist
- [ ] Class has clear invariant
- [ ] Rule of zero/five followed
- [ ] No raw pointers as members
- [ ] Virtual destructor for base classes

---

## C: Classes and Class Hierarchies

### C.1: Organize related data into structures (`struct`s or `class`es)

**Reason:** Ease of comprehension. Related data should be reflected in code.

**Example - Bad:**
```cpp
void draw(int x, int y, int x2, int y2);
```

**Example - Good:**
```cpp
void draw(Point from, Point to);
```

**Enforcement:** Probably impossible.

---

### C.2: Use `class` if the class has an invariant; use `struct` if data members can vary independently

**Reason:** Readability. `class` alerts programmer to need for invariant.

**Example:**
```cpp
struct Pair {  // members vary independently
    string name;
    int volume;
};

class Date {  // has invariant
public:
    Date(int yy, Month mm, char dd);
private:
    int y;
    Month m;
    char d;
};
```

**Enforcement:** Not specified.

---

### C.3: Represent the distinction between an interface and an implementation using a class

**Reason:** Explicit separation helps maintenance and design.

**Example:**
```cpp
class Date {
public:
    Date();
    Date(int yy, Month mm, char dd);
    // ... public interface ...
private:
    // ... representation ...
};
```

**Enforcement:** Not specified.

---

### C.4: Make a function a member only if it needs direct access to class representation

**Reason:** Less coupling than member functions. Non-member functions don't require class definition.

**Example - Bad:**
```cpp
class String {
public:
    String& operator+=(const String& s)  // bad: doesn't need access
    { /* ... */ return *this; }
};
```

**Example - Good:**
```cpp
class String {
public:
    String& operator+=(const String& s) { /* direct access */ return *this; }
};
String operator+(const String& a, const String& b) { String res = a; res += b; return res; }
```

**Enforcement:** Not specified.

---

### C.5: Place helper functions in the same namespace as the class they support

**Reason:** Helper functions that don't need direct access to class representation can still be associated with the class.

**Example:**
```cpp
namespace Chrono {
    class Date { /* ... */ };
    bool operator==(const Date&, const Date&);  // helper in same namespace
}
```

**Enforcement:** Not specified.

---

### C.7: Don't define a class or enum and declare a variable of its type in the same statement

**Reason:** Splitting declaration improves readability and allows proper forward declaration.

**Example - Bad:**
```cpp
struct Date { int d, m, y; } date1, date2;
```

**Example - Good:**
```cpp
struct Date { int d, m, y; };
Date date1, date2;
```

**Enforcement:** Flag definitions and declarations in same statement.

---

### C.8: Use `class` rather than `struct` if any member is non-public

**Reason:** Convention - `class` indicates presence of private interface.

**Example:**
```cpp
struct S { int x; };  // all public
class C { int x; };   // private by default
```

**Enforcement:** Flag structs with private members.

---

### C.9: Minimize exposure of members

**Reason:** Encapsulation. Prevents accidental modification.

**Example:**
```cpp
class Foo {
public:
    int x;  // bad: exposes member
private:
    int y;  // good
protected:
    int z;  // better than public, but still exposed
};
```

**Enforcement:** Not specified.

---

### C.10: Prefer concrete types over class hierarchies

**Reason:** Concrete types are simpler, more efficient, and easier to reason about.

**Example:**
```cpp
struct Point {
    int x, y;
    // operations...
};
```

**Enforcement:** Not specified.

---

### C.11: Make concrete types regular

**Reason:** Regular types behave like built-in types - can be copied, moved, compared.

**Example:**
```cpp
struct Date {
    int y, m, d;
    Date(int y, int m, int d);
    bool operator==(const Date&) const;
    bool operator<(const Date&) const;
    // default copy/move/destruct OK
};
```

**Enforcement:** Not specified.

---

### C.12: Don't make data members `const` or references in a copyable or movable type

**Reason:** `const` and reference members make class non-assignable.

**Example - Bad:**
```cpp
class Foo {
    const int x;  // prevents assignment
    int& y;       // prevents assignment
};
```

**Enforcement:** Flag const/reference members in copyable/movable types.

---

### C.20: If you can avoid defining default operations, do

**Reason:** Compiler-generated defaults are correct and efficient.

**Example:**
```cpp
struct Named_map {
    // no need to declare default operations
    string name;
    map<string, int> rep;
};
```

**Enforcement:** Not specified.

---

### C.21: If you define or `=delete` any copy, move, or destructor, define or `=delete` them all

**Reason:** The Rule of Five - special member functions are related.

**Example:**
```cpp
class Movable {
public:
    Movable(const Movable&) = default;
    Movable& operator=(const Movable&) = default;
    Movable(Movable&&) noexcept = default;
    Movable& operator=(Movable&&) noexcept = default;
    ~Movable() = default;
};
```

**Enforcement:** Flag if any defined without others.

---

### C.22: Make default operations consistent

**Reason:** Default operations should have same semantics and access.

**Example:**
```cpp
class X {
public:
    X(const X&);           // copy
    X& operator=(X&&);     // move - inconsistent!
    X(X&&) noexcept;       // move
    X& operator=(const X&); // copy - inconsistent!
};
```

**Enforcement:** Flag inconsistent definitions.

---

### C.30: Define a destructor if a class needs an explicit action at object destruction

**Reason:** To release resources, commit transactions, log actions.

**Example:**
```cpp
class File {
    FILE* p;
public:
    ~File() { fclose(p); }  // close file
};
```

**Enforcement:** Not specified.

---

### C.31: All resources acquired by a class must be released by the class's destructor

**Reason:** Prevents resource leaks.

**Example:**
```cpp
class X {
    HANDLE h;
public:
    X() { h = CreateHandle(); }
    ~X() { CloseHandle(h); }  // must release
};
```

**Enforcement:** Flag resources not released in destructor.

---

### C.32: If a class has a raw pointer (`T*`) or reference (`T&`), consider whether it might be owning

**Reason:** Raw pointers often indicate ownership that should be explicit.

**Example:**
```cpp
class Widget {
    unique_ptr<Node> p;  // ownership explicit
    Node* q;             // non-owning pointer?
};
```

**Enforcement:** Flag raw pointer/reference members.

---

### C.33: If a class has an owning pointer member, define a destructor

**Reason:** Owner must delete owned object.

**Example:**
```cpp
class Foo {
    owner<int*> p;
public:
    ~Foo() { delete p; }
};
```

**Enforcement:** Flag owning pointer without destructor.

---

### C.35: A base class destructor should be either public and virtual, or protected and non-virtual

**Reason:** Prevents undefined behavior when deleting through base pointer.

**Example:**
```cpp
class Base {
public:
    virtual ~Base();  // OK: public virtual
};

class Base2 {
protected:
    ~Base2();  // OK: protected non-virtual
};
```

**Enforcement:** Flag base with non-virtual public destructor.

---

### C.36: A destructor must not fail

**Reason:** Throwing from destructor causes program termination or undefined behavior.

**Example:**
```cpp
class Foo {
public:
    ~Foo() noexcept { /* no throw */ }
};
```

**Enforcement:** Flag throwing operations in destructor.

---

### C.37: Make destructors `noexcept`

**Reason:** Destructors should never throw.

**Example:**
```cpp
class Foo {
public:
    ~Foo() noexcept { /* ... */ }
};
```

**Enforcement:** Flag destructors not marked `noexcept`.

---

### C.40: Define a constructor if a class has an invariant

**Reason:** Constructor must establish invariant for member functions to assume.

**Example:**
```cpp
class Date {
    int y, m, d;
public:
    Date(int y, int m, int d);  // validates and initializes
};
```

**Enforcement:** Not specified.

---

### C.41: A constructor should create a fully initialized object

**Reason:** Prevents use of partially constructed objects.

**Example:**
```cpp
class Foo {
    int x;
public:
    Foo(int xx) : x(xx) {}  // fully initialized
};
```

**Enforcement:** Flag uninitialized members.

---

### C.42: If a constructor cannot construct a valid object, throw an exception

**Reason:** Cannot return error code from constructor.

**Example:**
```cpp
class File {
    FILE* f;
public:
    File(const string& name) {
        f = fopen(name.c_str(), "r");
        if (!f) throw runtime_error("File not found");
    }
};
```

**Enforcement:** Not specified.

---

### C.43: Ensure that a copyable class has a default constructor

**Reason:** Allows use in containers and arrays.

**Example:**
```cpp
struct Point {
    int x = 0, y = 0;  // default constructor
};
```

**Enforcement:** Not specified.

---

### C.44: Prefer default constructors to be simple and non-throwing

**Reason:** Enables efficient use in containers and error handling.

**Example:**
```cpp
class Foo {
    int x = 0;
public:
    Foo() noexcept = default;  // simple, non-throwing
};
```

**Enforcement:** Flag throwing default constructors.

---

### C.45: Don't define a default constructor that only initializes data members; use default member initializers instead

**Reason:** Simpler and more consistent.

**Example - Bad:**
```cpp
class Foo {
    int x;
public:
    Foo() : x(0) {}
};
```

**Example - Good:**
```cpp
class Foo {
    int x = 0;
};
```

**Enforcement:** Flag constructors only initializing members.

---

### C.46: By default, declare single-argument constructors explicit

**Reason:** Prevents unintended implicit conversions.

**Example - Bad:**
```cpp
class Foo {
    int x;
public:
    Foo(int xx) : x(xx) {}  // allows implicit conversion
};
```

**Example - Good:**
```cpp
class Foo {
    int x;
public:
    explicit Foo(int xx) : x(xx) {}
};
```

**Enforcement:** Flag non-explicit single-arg constructors.

---

### C.47: Define and initialize data members in the order of member declaration

**Reason:** Members initialized in declaration order regardless of initializer list order.

**Example:**
```cpp
class Foo {
    int a;
    int b;
public:
    Foo() : a(1), b(2) {}  // matches declaration order
};
```

**Enforcement:** Flag mismatch with declaration order.

---

### C.48: Prefer default member initializers to member initializers in constructors for constant initializers

**Reason:** Less repetition, clearer intent.

**Example:**
```cpp
class Foo {
    int x = 0;  // default member initializer
public:
    Foo(int xx) : x(xx) {}
    Foo() = default;
};
```

**Enforcement:** Not specified.

---

### C.49: Prefer initialization to assignment in constructors

**Reason:** More efficient - direct initialization vs default construct + assign.

**Example - Bad:**
```cpp
class Foo {
    string s;
public:
    Foo(const string& s2) { s = s2; }  // assignment
};
```

**Example - Good:**
```cpp
class Foo {
    string s;
public:
    Foo(const string& s2) : s(s2) {}  // initialization
};
```

**Enforcement:** Flag assignment to members in constructor body.

---

### C.50: Use a factory function if you need "virtual behavior" during initialization

**Reason:** Constructors can't be virtual; factory function enables derived type creation.

**Example:**
```cpp
class Shape {
public:
    static unique_ptr<Shape> create(string type);  // factory
};
```

**Enforcement:** Not specified.

---

### C.51: Use delegating constructors to represent common actions for all constructors

**Reason:** Avoids code duplication.

**Example:**
```cpp
class Foo {
    int x;
    double y;
public:
    Foo(int xx, double yy) : x(xx), y(yy) {}
    Foo(int xx) : Foo(xx, 0.0) {}  // delegates
    Foo() : Foo(0, 0.0) {}         // delegates
};
```

**Enforcement:** Flag duplicated initialization code.

---

### C.52: Use inheriting constructors to import constructors into derived class

**Reason:** Avoids boilerplate forwarding.

**Example:**
```cpp
struct Derived : Base {
    using Base::Base;  // inherit all Base constructors
};
```

**Enforcement:** Not specified.

---

### C.60: Make copy assignment non-`virtual`, take parameter by `const&`, and return by non-`const&`

**Reason:** Conventional, efficient, consistent with built-in types.

**Example:**
```cpp
class Foo {
public:
    Foo& operator=(const Foo& other) {
        // copy
        return *this;
    }
};
```

**Enforcement:** Flag virtual copy assignment or wrong parameter/return type.

---

### C.61: A copy operation should copy

**Reason:** Surprise if copy does something else.

**Example:**
```cpp
class Foo {
    vector<int> v;
public:
    Foo& operator=(const Foo& other) {
        v = other.v;  // actual copy
        return *this;
    }
};
```

**Enforcement:** Not specified.

---

### C.62: Make copy assignment safe for self-assignment

**Reason:** `x = x;` should be valid and safe.

**Example - Bad:**
```cpp
Foo& operator=(const Foo& other) {
    delete p;       // bad if &other == this
    p = new int(*other.p);
    return *this;
}
```

**Example - Good:**
```cpp
Foo& operator=(const Foo& other) {
    if (this == &other) return *this;
    delete p;
    p = new int(*other.p);
    return *this;
}
```

**Enforcement:** Check for self-assignment safety.

---

### C.63: Make move assignment non-`virtual`, take parameter by `&&`, and return by non-`const&`

**Reason:** Consistent with copy assignment convention.

**Example:**
```cpp
class Foo {
public:
    Foo& operator=(Foo&& other) noexcept {
        // move
        return *this;
    }
};
```

**Enforcement:** Flag virtual move assignment or wrong parameter/return type.

---

### C.64: A move operation should move and leave its source in a valid state

**Reason:** Expected behavior of move operations.

**Example:**
```cpp
class Foo {
    unique_ptr<int> p;
public:
    Foo& operator=(Foo&& other) noexcept {
        p = move(other.p);  // other.p now nullptr
        return *this;
    }
};
```

**Enforcement:** Not specified.

---

### C.65: Make move assignment safe for self-assignment

**Reason:** Even if uncommon, `x = std::move(x);` should work.

**Example:**
```cpp
Foo& operator=(Foo&& other) noexcept {
    if (this == &other) return *this;
    p = move(other.p);
    return *this;
}
```

**Enforcement:** Check for self-assignment safety in move operations.

---

### C.66: Make move operations `noexcept`

**Reason:** Allows optimizations in containers and standard library.

**Example:**
```cpp
class Foo {
public:
    Foo(Foo&& other) noexcept { /* ... */ }
    Foo& operator=(Foo&& other) noexcept { /* ... */ return *this; }
};
```

**Enforcement:** Flag move operations not marked `noexcept`.

---

### C.67: A polymorphic class should suppress public copy/move

**Reason:** Slicing leads to bugs.

**Example:**
```cpp
class Base {
public:
    Base(const Base&) = delete;
    Base& operator=(const Base&) = delete;
    Base(Base&&) = delete;
    Base& operator=(Base&&) = delete;
    virtual ~Base() = default;
};
```

**Enforcement:** Flag public copy/move on polymorphic classes.

---

### C.80: Use `=default` if you have to be explicit about using the default semantics

**Reason:** Clearer intent, lets compiler know you want default.

**Example:**
```cpp
class Foo {
public:
    Foo(const Foo&) = default;
    Foo& operator=(const Foo&) = default;
    Foo(Foo&&) noexcept = default;
    Foo& operator=(Foo&&) noexcept = default;
    ~Foo() = default;
};
```

**Enforcement:** Not specified.

---

### C.81: Use `=delete` when you want to disable default behavior

**Reason:** Explicitly prevents unwanted operations.

**Example:**
```cpp
class Foo {
    unique_ptr<int> p;
public:
    Foo(const Foo&) = delete;  // no copy
    Foo& operator=(const Foo&) = delete;
};
```

**Enforcement:** Not specified.

---

### C.82: Don't call virtual functions in constructors and destructors

**Reason:** Called function is the one defined in current class, not overridden version.

**Example - Bad:**
```cpp
class Base {
public:
    Base() { f(); }  // calls Base::f, not Derived::f
    virtual void f() {}
};
```

**Enforcement:** Flag virtual function calls in constructors/destructors.

---

### C.83: For value-like types, consider providing a `noexcept` swap function

**Reason:** Enables efficient swapping and standard library compatibility.

**Example:**
```cpp
class Foo {
public:
    friend void swap(Foo& a, Foo& b) noexcept {
        using std::swap;
        swap(a.x, b.x);
        swap(a.y, b.y);
    }
};
```

**Enforcement:** Not specified.

---

### C.84: A `swap` function must not fail

**Reason:** Swap used in many contexts assuming no-throw.

**Example:**
```cpp
void swap(Foo& a, Foo& b) noexcept { /* no throw */ }
```

**Enforcement:** Flag throwing swap functions.

---

### C.85: Make `swap` `noexcept`

**Reason:** Enables optimizations and is expected convention.

**Example:**
```cpp
friend void swap(Foo& a, Foo& b) noexcept { /* ... */ }
```

**Enforcement:** Flag swap not marked `noexcept`.

---

### C.86: Make `==` symmetric with respect to operand types and `noexcept`

**Reason:** Expected behavior for comparison.

**Example:**
```cpp
class Foo {
public:
    bool operator==(const Foo& other) const noexcept {
        return /* ... */;
    }
};
```

**Enforcement:** Flag asymmetric or throwing `==`.

---

### C.87: Beware of `==` on base classes

**Reason:** Default `==` compares all members, not virtual.

**Example:**
```cpp
class Base {
    // Base::operator== compares Base members only
};
```

**Enforcement:** Warn on default `==` in polymorphic bases.

---

### C.89: Make a `hash` `noexcept`

**Reason:** Hash functions should not throw.

**Example:**
```cpp
struct FooHash {
    size_t operator()(const Foo& f) const noexcept {
        return /* ... */;
    }
};
```

**Enforcement:** Flag throwing hash functions.

---

### C.90: Rely on constructors and assignment operators, not `memset` and `memcpy`

**Reason:** Bypasses class invariants and type safety.

**Example - Bad:**
```cpp
Foo f;
memset(&f, 0, sizeof(f));  // BAD
```

**Example - Good:**
```cpp
Foo f{};  // proper initialization
```

**Enforcement:** Flag `memset`/`memcpy` on class types.

---

### C.120: Use class hierarchies to represent concepts with inherent hierarchical structure (only)

**Reason:** Hierarchies add complexity; use only when conceptually appropriate.

**Example:**
```cpp
class Shape { /* ... */ };
class Circle : public Shape { /* ... */ };
class Triangle : public Shape { /* ... */ };
```

**Enforcement:** Not specified.

---

### C.121: If a base class is used as an interface, make it a pure abstract class

**Reason:** Pure abstract classes have no data, only interface.

**Example:**
```cpp
class Shape {
public:
    virtual void draw() = 0;
    virtual ~Shape() = default;
};
```

**Enforcement:** Flag interface classes with data members.

---

### C.122: Use abstract classes as interfaces when complete separation of interface and implementation is needed

**Reason:** Enables decoupling and polymorphism.

**Example:**
```cpp
class IWidget {
public:
    virtual void draw() = 0;
    virtual ~IWidget() = default;
};
```

**Enforcement:** Not specified.

---

### C.126: An abstract class typically doesn't need a user-written constructor

**Reason:** Abstract classes have no data, only interface.

**Example:**
```cpp
class IWidget {
public:
    virtual void draw() = 0;
    virtual ~IWidget() = default;  // no constructor needed
};
```

**Enforcement:** Not specified.

---

### C.127: A class with a virtual function should have a virtual or protected destructor

**Reason:** Prevents undefined behavior when deleting through base pointer.

**Example:**
```cpp
class Base {
public:
    virtual void f() {}
    virtual ~Base() {}  // virtual destructor
};
```

**Enforcement:** Flag class with virtual function but non-virtual public destructor.

---

### C.128: Virtual functions should specify exactly one of `virtual`, `override`, or `final`

**Reason:** Clear intent, catches errors.

**Example:**
```cpp
class Derived : public Base {
    void f() override;  // clear: overrides Base::f
};
```

**Enforcement:** Flag missing `override` on overrides.

---

### C.129: When designing a class hierarchy, distinguish between implementation inheritance and interface inheritance

**Reason:** Avoids confusion and bad designs.

**Example:**
```cpp
// Interface inheritance
class IWidget {
public:
    virtual void draw() = 0;
};

// Implementation inheritance (private)
class Button : private IWidgetImpl { /* ... */ };
```

**Enforcement:** Not specified.

---

### C.130: For making deep copies of polymorphic classes prefer a virtual `clone` function instead of public copy construction/assignment

**Reason:** Prevents slicing, enables proper polymorphic copy.

**Example:**
```cpp
class Base {
public:
    virtual unique_ptr<Base> clone() const = 0;
};

class Derived : public Base {
public:
    unique_ptr<Base> clone() const override {
        return make_unique<Derived>(*this);
    }
};
```

**Enforcement:** Flag public copy in polymorphic classes.

---

### C.131: Avoid trivial getters and setters

**Reason:** Often indicates poor design - members should be public or have proper abstraction.

**Example - Bad:**
```cpp
class Foo {
    int x;
public:
    int getX() const { return x; }
    void setX(int xx) { x = xx; }
};
```

**Example - Good:**
```cpp
class Foo {
public:
    int x;  // just make it public
};
```

**Enforcement:** Warn on trivial getters/setters.

---

### C.132: Don't make a function `virtual` without reason

**Reason:** Virtual functions add cost and complexity.

**Enforcement:** Warn on unused override capability.

---

### C.133: Avoid `protected` data

**Reason:** Breaks encapsulation, complicates maintenance.

**Example - Bad:**
```cpp
class Base {
protected:
    int x;  // accessible to all derived classes
};
```

**Enforcement:** Flag protected data members.

---

### C.134: Ensure all non-`const` data members have the same access level

**Reason:** Simplifies reasoning about state.

**Example:**
```cpp
class Foo {
private:
    int x, y, z;  // all same level
};
```

**Enforcement:** Flag mixed access levels for non-const members.

---

### C.135: Use multiple inheritance to represent multiple distinct interfaces

**Reason:** MI for interfaces is safe and useful.

**Example:**
```cpp
class IReadable {
public:
    virtual void read() = 0;
};

class IWritable {
public:
    virtual void write() = 0;
};

class File : public IReadable, public IWritable { /* ... */ };
```

**Enforcement:** Not specified.

---

### C.136: Use multiple inheritance to represent the union of implementation attributes

**Reason:** Can combine useful implementations.

**Example:**
```cpp
class Synced : public Mutex, public Data { /* ... */ };
```

**Enforcement:** Not specified.

---

### C.137: Use `virtual` bases to avoid overly general base classes

**Reason:** Prevents diamond problem ambiguity.

**Example:**
```cpp
class Base { /* ... */ };
class D1 : virtual public Base { /* ... */ };
class D2 : virtual public Base { /* ... */ };
class Derived : public D1, public D2 { /* ... */ };  // single Base subobject
```

**Enforcement:** Not specified.

---

### C.138: Create an overload set for a derived class and its bases with `using`

**Reason:** Makes base functions visible in derived class.

**Example:**
```cpp
class Base {
public:
    void f(int);
    void f(double);
};

class Derived : public Base {
public:
    using Base::f;  // brings both overloads
    void f(string);
};
```

**Enforcement:** Not specified.

---

### C.139: Use `final` on classes sparingly

**Reason:** `final` prevents derivation - only use when intentional.

**Example:**
```cpp
class Utility final { /* ... */ };  // not meant to be derived from
```

**Enforcement:** Not specified.

---

### C.140: Do not provide different default arguments for a virtual function and an overrider

**Reason:** Default arguments are bound statically, not dynamically - causes bugs.

**Example - Bad:**
```cpp
class Base {
public:
    virtual void f(int x = 5);
};

class Derived : public Base {
public:
    void f(int x = 10) override;  // BAD: different default
};
```

**Enforcement:** Flag mismatched default arguments on overrides.

---

### C.145: Access polymorphic objects through pointers and references

**Reason:** Prevents object slicing.

**Example - Bad:**
```cpp
Circle c;
Shape s = c;  // BAD: slices off Circle part
```

**Example - Good:**
```cpp
Circle c;
Shape& s = c;  // OK: reference
Shape* p = &c;  // OK: pointer
```

**Enforcement:** Warn on slicing assignments.

---

### C.146: Use `dynamic_cast` where class hierarchy navigation is unavoidable

**Reason:** Type-safe downcasting when needed.

**Example:**
```cpp
if (auto* derived = dynamic_cast<Derived*>(base)) {
    // use derived interface
}
```

**Enforcement:** Not specified.

---

### C.147: Use `dynamic_cast` to a reference type when failure is an error

**Reason:** `dynamic_cast` to reference throws on failure.

**Example:**
```cpp
Derived& d = dynamic_cast<Derived&>(base);  // throws if fails
```

**Enforcement:** Not specified.

---

### C.148: Use `dynamic_cast` to a pointer type when failure is valid

**Reason:** `dynamic_cast` to pointer returns nullptr on failure.

**Example:**
```cpp
if (auto* derived = dynamic_cast<Derived*>(base)) {
    // valid conversion
}
```

**Enforcement:** Not specified.

---

### C.149: Use `unique_ptr` or `shared_ptr` to avoid forgetting to `delete` objects created using `new`

**Reason:** Automatic memory management.

**Example:**
```cpp
auto p = make_unique<Derived>();  // automatic deletion
```

**Enforcement:** Flag raw `new` without assignment to smart pointer or `owner`.

---

### C.150: Use `make_unique()` to construct objects owned by `unique_ptr`s

**Reason:** More efficient (one allocation) and exception-safe.

**Example:**
```cpp
auto p = make_unique<Derived>(args);  // good
auto p = unique_ptr<Derived>{new Derived(args)};  // less efficient
```

**Enforcement:** Not specified.

---

### C.151: Use `make_shared()` to construct objects owned by `shared_ptr`s

**Reason:** Single allocation for object and refcount.

**Example:**
```cpp
auto p = make_shared<Derived>(args);
```

**Enforcement:** Not specified.

---

### C.152: Never assign a pointer to an array of derived class objects to a pointer to its base

**Reason:** Pointer arithmetic breaks due to different sizes.

**Example - Bad:**
```cpp
Derived arr[10];
Base* p = arr;  // BAD
p[1];  // undefined behavior
```

**Enforcement:** Flag array-to-base-pointer conversions.

---

### C.153: Prefer virtual function to casting

**Reason:** Virtual functions are type-safe and more maintainable.

**Example - Bad:**
```cpp
if (auto* d = dynamic_cast<D*>(base)) {
    d->do_d();
} else if (auto* e = dynamic_cast<E*>(base)) {
    e->do_e();
}
```

**Example - Good:**
```cpp
base->do_it();  // virtual function
```

**Enforcement:** Warn on chains of dynamic_cast that could be virtual functions.
