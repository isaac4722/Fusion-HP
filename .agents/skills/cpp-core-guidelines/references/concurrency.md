# Concurrency and Thread Safety

Use this reference when reviewing multi-threaded code, checking for data races, or designing concurrent systems.

## Quick Checklist
- [ ] No data races
- [ ] Proper synchronization (locks, atomics)
- [ ] lock_guard/scoped_lock used
- [ ] No deadlocks
- [ ] RAII for locks (never plain lock()/unlock())
- [ ] Condition variables have predicates
- [ ] No detached threads without good reason
- [ ] Mutex defined with data it guards

---

## CP: Concurrency and Parallelism

### CP.1: Assume that your code will run as part of a multi-threaded program

**Reason**
It's hard to be certain that concurrency isn't used now or won't be used sometime in the future. Code gets reused.

**Example, bad**
```cpp
double cached_computation(int x)
{
    // bad: these statics cause data races in multi-threaded usage
    static int cached_x = 0.0;
    static double cached_result = COMPUTATION_OF_ZERO;

    if (cached_x != x) {
        cached_x = x;
        cached_result = computation(x);
    }
    return cached_result;
}
```

**Example, good**
```cpp
struct ComputationCache {
    int cached_x = 0;
    double cached_result = COMPUTATION_OF_ZERO;

    double compute(int x) {
        if (cached_x != x) {
            cached_x = x;
            cached_result = computation(x);
        }
        return cached_result;
    }
};
```

**Enforcement**
None specified.

---

### CP.2: Avoid data races

**Reason**
Unless you do, nothing is guaranteed to work and subtle errors will persist.

**Example, bad**
```cpp
int get_id()
{
    static int id = 1;
    return id++;  // data race
}
```

**Note**
If two threads can access the same object concurrently (without synchronization), and at least one is a writer, you have a data race.

**Enforcement**
Some is possible. Use static analysis tools (clang/GCC thread safety annotations) and dynamic tools (ThreadSanitizer).

---

### CP.3: Minimize explicit sharing of writable data

**Reason**
If you don't share writable data, you can't have a data race. The less sharing you do, the less chance you have to forget to synchronize access.

**Example**
```cpp
void process_readings(const vector<Reading>& surface_readings)
{
    auto h1 = async([&] { if (!validate(surface_readings)) throw Invalid_data{}; });
    auto h2 = async([&] { return temperature_gradients(surface_readings); });
    auto h3 = async([&] { return altitude_map(surface_readings); });
    // ...
}
```

Without `const`, we would have to review every asynchronously invoked function for potential data races.

**Note**
Immutable data can be safely and efficiently shared. No locking is needed.

---

### CP.4: Think in terms of tasks, rather than threads

**Reason**
A `thread` is an implementation concept. A task is an application notion, something you'd like to do concurrently.

**Example**
```cpp
std::thread publisher([=] { std::cout << msg; });      // bad: less expressive
auto pubtask = std::async([=] { std::cout << msg; });  // OK
```

---

### CP.8: Don't try to use `volatile` for synchronization

**Reason**
In C++, `volatile` does not provide atomicity, does not synchronize between threads, and does not prevent instruction reordering. It has nothing to do with concurrency.

**Example, bad**
```cpp
volatile int free_slots = max_slots;  // does NOT fix the race

Pool* use()
{
    if (int n = free_slots--) return &pool[n];  // still a data race
}
```

**Alternative**
Use `atomic` types where you might have used `volatile` in some other language:
```cpp
atomic<int> free_slots = max_slots;  // correct
```

---

### CP.9: Whenever feasible use tools to validate your concurrent code

**Reason**
Concurrent code is exceptionally hard to get right and testing is less effective at finding concurrency errors.

**Tools**
* Static enforcement: Clang Thread Safety Analysis, GCC thread safety annotations
* Dynamic enforcement: Clang Thread Sanitizer (TSan)

---

### CP.20: Use RAII, never plain `lock()`/`unlock()`

**Reason**
Avoids nasty errors from unreleased locks.

**Example, bad**
```cpp
mutex mtx;

void do_stuff()
{
    mtx.lock();
    // ... do stuff ...
    mtx.unlock();  // someone will forget this
}
```

**Example, good**
```cpp
void do_stuff()
{
    unique_lock<mutex> lck {mtx};
    // ... do stuff ...
}  // automatic unlock
```

**Enforcement**
Flag calls of member `lock()` and `unlock()`.

---

### CP.21: Use `std::lock()` or `std::scoped_lock` to acquire multiple `mutex`es

**Reason**
To avoid deadlocks on multiple `mutex`es.

**Example, bad** (deadlock risk)
```cpp
// thread 1
lock_guard<mutex> lck1(m1);
lock_guard<mutex> lck2(m2);

// thread 2
lock_guard<mutex> lck2(m2);
lock_guard<mutex> lck1(m1);
```

**Example, good**
```cpp
// thread 1
scoped_lock<mutex, mutex> lck1(m1, m2);

// thread 2
scoped_lock<mutex, mutex> lck2(m2, m1);
```

---

### CP.22: Never call unknown code while holding a lock (e.g., a callback)

**Reason**
If you don't know what a piece of code does, you are risking deadlock.

**Example**
```cpp
void do_this(Foo* p)
{
    lock_guard<mutex> lck {my_mutex};
    // ... do something ...
    p->act(my_data);  // might call do_this recursively -> deadlock
}
```

**Enforcement**
* Flag calling a virtual function with a non-recursive `mutex` held
* Flag calling a callback with a non-recursive `mutex` held

---

### CP.23: Think of a joining `thread` as a scoped container

**Reason**
To maintain pointer safety and avoid leaks. If a `thread` joins, we can safely pass pointers to objects in the scope of the `thread`.

**Example**
```cpp
void some_fct(int* p)
{
    int x = 77;
    joining_thread t0(f, &x);           // OK
    joining_thread t1(f, p);            // OK
    int glob = 33;
    joining_thread t2(f, &glob);        // OK
}
```

---

### CP.24: Think of a `thread` as a global container

**Reason**
If a `thread` is detached, we can safely pass pointers to static and free store objects only.

**Example**
```cpp
int glob = 33;

void some_fct(int* p)
{
    int x = 77;
    std::thread t0(f, &x);           // bad - local variable
    std::thread t1(f, p);            // bad - might outlive
    std::thread t2(f, &glob);        // OK - static
    t0.detach();
}
```

---

### CP.25: Prefer `gsl::joining_thread` over `std::thread`

**Reason**
A `joining_thread` is a thread that joins at the end of its scope. Detached threads are hard to monitor.

**Example, bad**
```cpp
int main()
{
    std::thread t1{f};
    std::thread t2{F()};
}  // spot the bugs - no join!
```

**Enforcement**
Flag uses of `std::thread`; suggest `gsl::joining_thread` or C++20 `std::jthread`.

---

### CP.26: Don't `detach()` a thread

**Reason**
Implementing lifetime control by `detach` makes it harder to monitor and communicate with the detached thread.

**Example**
```cpp
void use()
{
    std::thread t(heartbeat);
    t.detach();  // hard to monitor
}
```

**Alternative**
Control lifetime by placing thread in outer scope:
```cpp
gsl::joining_thread t(heartbeat);  // runs for as long as program does
```

**Enforcement**
Flag `detach()`.

---

### CP.31: Pass small amounts of data between threads by value, rather than by reference or pointer

**Reason**
A small amount of data is cheaper to copy than to share using locking. Copying gives unique ownership and eliminates data races.

**Example**
```cpp
string modify1(string);  // pass by value - simple
void modify2(string&);   // pass by reference - needs locking

void fct(string& s)
{
    auto res = async(modify1, s);  // copies data
    async(modify2, s);              // needs synchronization
}
```

---

### CP.32: To share ownership between unrelated `thread`s use `shared_ptr`

**Reason**
If threads need to share free store memory that needs deletion, `shared_ptr` ensures proper deletion.

---

### CP.40: Minimize context switching

**Reason**
Context switches are expensive.

---

### CP.41: Minimize thread creation and destruction

**Reason**
Thread creation is expensive.

**Example, bad**
```cpp
void dispatcher(istream& is)
{
    for (Message m; is >> m; )
        run_list.push_back(new thread(worker, m));  // new thread per message
}
```

**Example, good** (use worker pool)
```cpp
Sync_queue<Message> work;

void dispatcher(istream& is)
{
    for (Message m; is >> m; )
        work.put(m);
}

void worker()
{
    for (Message m; m = work.get(); ) {
        // process
    }
}
```

---

### CP.42: Don't `wait` without a condition

**Reason**
A `wait` without a condition can miss a wakeup or wake up to find no work to do.

**Example, bad**
```cpp
void thread2()
{
    while (true) {
        std::unique_lock<std::mutex> lock(mx);
        cv.wait(lock);    // might block forever
    }
}
```

**Example, good**
```cpp
template<typename T>
void Sync_queue<T>::get(T& val)
{
    unique_lock<mutex> lck(mtx);
    cond.wait(lck, [this] { return !q.empty(); });  // prevent spurious wakeup
    val = q.front();
    q.pop_front();
}
```

**Enforcement**
Flag all `wait`s without conditions.

---

### CP.43: Minimize time spent in a critical section

**Reason**
Less time with a `mutex` means less waiting by other threads.

**Example, bad**
```cpp
void do_something()
{
    unique_lock<mutex> lck(my_lock);
    do0();  // preparation: does not need lock
    do1();  // transaction: needs locking
    do2();  // cleanup: does not need locking
}
```

**Example, good**
```cpp
void do_something()
{
    do0();  // preparation
    {
        unique_lock<mutex> lck(my_lock);
        do1();  // transaction only
    }
    do2();  // cleanup
}
```

---

### CP.44: Remember to name your `lock_guard`s and `unique_lock`s

**Reason**
An unnamed local object is a temporary that immediately goes out of scope.

**Example, bad**
```cpp
void f()
{
    unique_lock<mutex>(m1);  // creates default-constructed local, shadows global
    lock_guard<mutex> {m2};  // temporary locks then immediately unlocks
    // neither mutex is locked here
}
```

**Enforcement**
Flag all unnamed `lock_guard`s and `unique_lock`s.

---

### CP.50: Define a `mutex` together with the data it guards. Use `synchronized_value<T>` where possible

**Reason**
It should be obvious that the data is to be guarded and how.

**Example**
```cpp
struct Record {
    std::mutex m;   // take this mutex before accessing other members
    // ...
};

class MyClass {
    synchronized_value<DataRecord> data; // Protect the data with a mutex
};
```

---

### CP.51: Do not use capturing lambdas that are coroutines

**Reason**
Captures will be destroyed after the closure goes out of scope, causing use-after-free after co_await.

**Example, bad**
```cpp
int value = get_value();
const auto lambda = [value]() -> std::future<void>
{
    co_await something();
    // "value" has been destroyed - use after free
};
lambda();
```

**Example, good**
```cpp
const auto lambda = [](auto value) -> std::future<void>
{
    co_await something();
    // value is still valid
};
lambda(value);
```

**Enforcement**
Flag a lambda that is a coroutine and has a non-empty capture list.

---

### CP.52: Do not hold locks or other synchronization primitives across suspension points

**Reason**
Creates deadlock risk and hurts performance (lock held while coroutine is suspended).

**Example, bad**
```cpp
std::future<void> do_something()
{
    std::lock_guard<std::mutex> guard(g_lock);
    co_await something();  // DANGER: holding lock while suspended
}
```

**Example, good**
```cpp
std::future<void> do_something()
{
    {
        std::lock_guard<std::mutex> guard(g_lock);
        // modify data
    }
    co_await something();  // OK: lock released
}
```

---

### CP.53: Parameters to coroutines should not be passed by reference

**Reason**
After first suspension point, reference parameters are dangling.

**Example, bad**
```cpp
std::future<int> do_something(const std::shared_ptr<int>& input)
{
    co_await something();
    co_return *input + 1;  // DANGER: dangling reference
}
```

**Example, good**
```cpp
std::future<int> do_something(std::shared_ptr<int> input)
{
    co_await something();
    co_return *input + 1;  // input is a copy, still valid
}
```

---

### CP.60: Use a `future` to return a value from a concurrent task

**Reason**
A `future` preserves usual function call return semantics. No explicit locking needed.

---

### CP.61: Use `async()` to spawn concurrent tasks

**Reason**
Avoid raw threads and raw promises. Use `std::async` which handles spawning/reusing threads.

**Example**
```cpp
void async_example()
{
    std::future<int> f1 = std::async(read_value, "v1.txt");
    std::future<int> f2 = std::async(read_value, "v2.txt");
    std::cout << f1.get() + f2.get() << '\n';
}
```

---

### CP.100: Don't use lock-free programming unless you absolutely have to

**Reason**
It's error-prone and requires expert-level knowledge.

**Example, bad** (has subtle bug - ABA problem)
```cpp
extern atomic<Link*> head;

Link* nh = new Link(data, nullptr);
Link* h = head.load();

do {
    if (h->data <= data) break;
    nh->next = h;
} while (!head.compare_exchange_weak(h, nh));
```

**Exception**
Atomic variables using sequentially consistent memory model (default) are safe.

---

### CP.101: Distrust your hardware/compiler combination

**Reason**
Low-level hardware interfaces are among the hardest to implement well.

**Note**
Instruction reordering makes lock-free programming difficult. Testing to extreme extent is essential.

---

### CP.102: Carefully study the literature

**Reason**
With the exception of atomics and a few patterns, lock-free programming is expert-only.

**References**
* Anthony Williams: C++ Concurrency in Action
* Boehm, Adve: "You Don't Know Jack About Shared Variables or Memory Models"
* Various papers on memory models and lock-free algorithms

---

### CP.110: Do not write your own double-checked locking for initialization

**Reason**
Since C++11, static local variables are initialized in a thread-safe way.

**Example**
```cpp
void f()
{
    static My_class my_object;  // Constructor called only once, thread-safe
    // ...
}
```

Or use `std::call_once`:
```cpp
void f()
{
    static std::once_flag my_once_flag;
    std::call_once(my_once_flag, []() {
        // do this only once
    });
}
```

---

### CP.111: Use a conventional pattern if you really need double-checked locking

**Reason**
Double-checked locking is easy to mess up. Use `atomic<bool>` not `volatile`.

**Example, bad**
```cpp
mutex action_mutex;
volatile bool action_needed;  // wrong

if (action_needed) {
    lock_guard<mutex> lock(action_mutex);
    if (action_needed) { /* ... */ }
}
```

**Example, good**
```cpp
mutex action_mutex;
atomic<bool> action_needed;  // correct

if (action_needed) {
    lock_guard<mutex> lock(action_mutex);
    if (action_needed) { /* ... */ }
}
```

---

### CP.200: Use `volatile` only to talk to non-C++ memory

**Reason**
`volatile` refers to objects shared with hardware or non-C++ code that doesn't follow C++ memory model.

**Example**
```cpp
const volatile long clock;  // hardware register
```

**Enforcement**
Flag `volatile T` local and data members; you likely intended `atomic<T>`.
