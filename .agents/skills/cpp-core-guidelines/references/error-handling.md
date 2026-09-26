# Error Handling

Use this reference when deciding how to handle errors, designing exception safety, or choosing between exceptions and error codes.

## Quick Checklist
- [ ] Exceptions for exceptional conditions
- [ ] No error codes for expected failures
- [ ] RAII for exception safety
- [ ] noexcept where appropriate

---

## E: Error Handling

### E.1: Develop an error-handling strategy early in a design

**Reason**
A consistent and complete strategy for handling errors and resource leaks is hard to retrofit into a system.

---

### E.2: Throw an exception to signal that a function can't perform its assigned task

**Reason**
To make error handling systematic, robust, and non-repetitive.

**Example**
```cpp
struct Foo {
    vector<Thing> v;
    File_handle f;
    string s;
};

void use()
{
    Foo bar { {Thing{1}, Thing{2}, Thing{monkey} }, {"my_file", "r"}, "Here we go!"};
    // ...
}
```

If construction fails, exceptions are thrown and `Foo`'s constructor correctly destroys constructed members.

---

### E.3: Use exceptions for error handling only

**Reason**
To keep error handling separated from "ordinary code." C++ implementations are optimized for exceptions being rare.

**Example, bad**
```cpp
int find_index(vector<string>& vec, const string& x)
{
    try {
        for (gsl::index i = 0; i < vec.size(); ++i)
            if (vec[i] == x) throw i;  // bad: not exceptional
    }
    catch (int i) {
        return i;
    }
    return -1;
}
```

---

### E.4: Design your error-handling strategy around invariants

**Reason**
To use an object it must be in a valid state (invariant). To recover from an error, every object must be in a valid state.

---

### E.5: Let a constructor establish an invariant, and throw if it cannot

**Reason**
Leaving an object without its invariant established is asking for trouble.

**Example**
```cpp
class Vector {
public:
    Vector(int s) : elem{new double[s]}, sz{s} { /* initialize */ }
    ~Vector() { delete [] elem; }
    // ...
private:
    owner<double*> elem;
    int sz;
};
```

`new` throws if it cannot allocate memory. The invariant is established by constructors.

**Enforcement**
Flag classes with `private` state without a constructor.

---

### E.6: Use RAII to prevent leaks

**Reason**
Leaks are typically unacceptable. Manual resource release is error-prone. RAII is the simplest, most systematic way.

**Example, bad**
```cpp
void f1(int i)
{
    int* p = new int[12];
    // ...
    if (i < 17) throw Bad{"in f()", i};  // leak
}
```

**Example, good**
```cpp
void f3(int i)
{
    auto p = make_unique<int[]>(12);
    // ...
    if (i < 17) throw Bad{"in f()", i};  // automatic cleanup
}

void f5(int i)
{
    vector<int> v(12);  // even better
    // ...
}
```

---

### E.7: State your preconditions

**Reason**
To avoid interface errors.

---

### E.8: State your postconditions

**Reason**
To avoid interface errors.

---

### E.12: Use `noexcept` when exiting a function because of a `throw` is impossible or unacceptable

**Reason**
To make error handling systematic, robust, and efficient.

**Example**
```cpp
double compute(double d) noexcept
{
    return log(sqrt(d <= 0 ? 1 : d));
}
```

**Note**
Many standard-library functions are `noexcept`. If no exception can be thrown, use `noexcept` instead of exception specifications.

---

### E.13: Never throw while being the direct owner of an object

**Reason**
That would be a leak.

**Example, bad**
```cpp
void leak(int x)
{
    auto p = new int{7};
    if (x < 0) throw Get_me_out_of_here{};  // leak *p
    delete p;
}
```

**Example, good**
```cpp
void no_leak(int x)
{
    auto p = make_unique<int>(7);
    if (x < 0) throw Get_me_out_of_here{};  // will delete *p
}
```

---

### E.14: Use purpose-designed user-defined types as exceptions (not built-in types)

**Reason**
A user-defined type can better transmit information about an error.

**Example, bad**
```cpp
throw 7;                      // bad
throw "something bad";        // bad
throw std::exception{};       // bad - no info
```

**Example, good**
```cpp
class MyException : public std::runtime_error {
public:
    MyException(const string& msg) : std::runtime_error{msg} {}
};

throw MyException{"something bad"};  // good

throw std::runtime_error("something bad");  // also good
```

**Enforcement**
Catch `throw` of built-in types and `std::exception`.

---

### E.15: Throw by value, catch exceptions from a hierarchy by reference

**Reason**
Throwing by value and catching by reference prevents copying and slicing.

**Example, bad**
```cpp
throw new widget{};           // bad: throw by value
catch (base_class e) { }      // bad: might slice
```

**Example, good**
```cpp
catch (base_class& e) { }     // good
catch (const base_class& e) { }  // better
```

**Note**
To rethrow, use `throw;` not `throw e;`.

**Enforcement**
* Flag catching by value of a type with virtual function
* Flag throwing raw pointers

---

### E.16: Destructors, deallocation, `swap`, and exception type copy/move construction must never fail

**Reason**
We don't know how to write reliable programs if these operations fail.

**Example, bad**
```cpp
class Connection {
public:
    ~Connection()
    {
        if (cannot_disconnect()) throw I_give_up{};  // very bad
    }
};
```

**Note**
* Deallocation functions must be `noexcept`
* `swap` functions must be `noexcept`
* Most destructors are implicitly `noexcept`
* Make move operations `noexcept`
* Exception types should have `noexcept` copy constructor

**Enforcement**
* Catch destructors, deallocation operations, and `swap`s that `throw`
* Catch such operations that are not `noexcept`

---

### E.17: Don't try to catch every exception in every function

**Reason**
Catching in a function that can't take meaningful recovery action leads to complexity. Let exceptions propagate.

**Example, bad**
```cpp
void f()
{
    try {
        // ...
    }
    catch (...) {
        throw;  // no action - just rethrowing
    }
}
```

---

### E.18: Minimize the use of explicit `try`/`catch`

**Reason**
`try`/`catch` is verbose and error-prone. Often a sign of unsystematic resource management.

**Example, bad**
```cpp
void f(zstring s)
{
    Gadget* p;
    try {
        p = new Gadget(s);
        // ...
        delete p;
    }
    catch (Gadget_construction_failure) {
        delete p;
        throw;
    }
}
```

**Example, good**
```cpp
void f2(zstring s)
{
    Gadget g {s};  // RAII handles cleanup
}
```

**Alternatives**
* proper resource handles and RAII
* `final_action` from GSL

---

### E.19: Use a `final_action` object to express cleanup if no suitable resource handle is available

**Reason**
`finally` from GSL is less verbose and harder to get wrong than `try`/`catch`.

**Example**
```cpp
void f(int n)
{
    void* p = malloc(n);
    auto _ = gsl::finally([p] { free(p); });
    // ...
}
```

**Note**
`finally` is a last resort. Prefer proper resource management objects.

---

### E.25: If you can't throw exceptions, simulate RAII for resource management

**Reason**
Even without exceptions, RAII is usually the best way of dealing with resources.

**Example**
```cpp
error_indicator func(zstring arg)
{
    Gadget g {arg};
    if (!g.valid()) return gadget_construction_error;
    // ...
    return 0;  // zero indicates "good"
}
```

**Note**
Prefer to use exceptions. Non-exception error handling requires explicit `valid()` checks that can be forgotten.

---

### E.26: If you can't throw exceptions, consider failing fast

**Reason**
If you can't recover, at least get out before consequential damage is done.

**Example**
```cpp
void f(int n)
{
    p = static_cast<X*>(malloc(n * sizeof(X)));
    if (!p) abort();  // abort if memory exhausted
    // ...
}
```

---

### E.27: If you can't throw exceptions, use error codes systematically

**Reason**
Systematic use minimizes the chance of forgetting to handle an error.

**Example**
```cpp
Gadget make_gadget(int n)
{
    // ...
}

void user()
{
    Gadget g = make_gadget(17);
    if (!g.valid()) {
        // error handling
    }
}
```

**Note**
Returning a pair of values is another approach:
```cpp
std::pair<Gadget, error_indicator> make_gadget(int n);

auto r = make_gadget(17);
if (!r.second) { /* error */ }
```

---

### E.28: Avoid error handling based on global state (e.g. `errno`)

**Reason**
Global state is hard to manage and easy to forget to check.

**Example, bad**
```cpp
int last_err;

void f(int n)
{
    p = static_cast<X*>(malloc(n * sizeof(X)));
    if (!p) last_err = -1;  // error based on global state
}
```

---

### E.30: Don't use exception specifications

**Reason**
Exception specifications make error handling brittle, impose run-time cost, and have been removed from C++.

**Example, bad**
```cpp
int use(int arg)
    throw(X, Y)  // bad: deprecated exception specification
{
    // ...
}
```

**Note**
If no exception can be thrown, use `noexcept`.

**Enforcement**
Flag every exception specification.

---

### E.31: Properly order your `catch`-clauses

**Reason**
`catch`-clauses are evaluated in order. One clause can hide another.

**Example, bad**
```cpp
void f()
{
    try {
        // ...
    }
    catch (Base& b) { /* ... */ }
    catch (Derived& d) { /* ... */ }  // never invoked
    catch (...) { /* ... */ }
    catch (std::exception& e) { /* ... */ }  // never invoked
}
```

**Enforcement**
Flag all "hiding handlers."
