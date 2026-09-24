# Performance and C-style Code

Use this reference when optimizing performance or dealing with C compatibility.

## Quick Checklist
- [ ] Avoid premature optimization
- [ ] Prefer C++ constructs over C
- [ ] Profile before optimizing
- [ ] Design for performance where needed

---

## Per: Performance

This section contains rules for people who need high performance or low-latency. The rules are more restrictive than what is needed for many applications.

### Per.1: Don't optimize without reason

**Reason**: If there is no need for optimization, the main result of the effort will be more errors and higher maintenance costs.

**Enforcement**: N/A

---

### Per.2: Don't optimize prematurely

**Reason**: Elaborately optimized code is usually larger and harder to change than unoptimized code.

**Enforcement**: N/A

---

### Per.3: Don't optimize something that's not performance critical

**Reason**: Optimizing a non-performance-critical part of a program has no effect on system performance.

**Note**: If your program spends 4% of its time doing computation A and 40% doing computation B, a 50% improvement on A is only as impactful as a 5% improvement on B.

**Enforcement**: N/A

---

### Per.4: Don't assume that complicated code is necessarily faster than simple code

**Reason**: Simple code can be very fast. Optimizers sometimes do marvels with simple code.

**Example, good**:
```cpp
vector<uint8_t> v(100000);
for (auto& c : v)
    c = ~c;
```

**Example, bad** (often slower):
```cpp
vector<uint8_t> v(100000);
for (size_t i = 0; i < v.size(); i += sizeof(uint64_t)) {
    uint64_t& quad_word = *reinterpret_cast<uint64_t*>(&v[i]);
    quad_word = ~quad_word;
}
```

**Enforcement**: N/A

---

### Per.5: Don't assume that low-level code is necessarily faster than high-level code

**Reason**: Low-level code sometimes inhibits optimizations. Optimizers sometimes do marvels with high-level code.

**Enforcement**: N/A

---

### Per.6: Don't make claims about performance without measurements

**Reason**: The field of performance is littered with myth and bogus folklore. Modern hardware and optimizers defy naive assumptions.

**Note**: A few simple microbenchmarks using Unix `time` or `<chrono>` can help dispel the most obvious myths. A profiler can help tell you which parts of your system are performance critical.

**Enforcement**: N/A

---

### Per.7: Design to enable optimization

**Reason**: Because we often need to optimize the initial design. Because a design that ignores the possibility of later improvement is hard to change.

**Note**: Aim to build a set of habits that by default results in efficient, maintainable, and optimizable code. Consider:
- Information passing: Prefer clean interfaces carrying sufficient information for later improvement
- Compact data: Use `std::vector` and access it systematically
- Conventional ways of passing information
- Don't overgeneralize
- Use libraries with good interfaces

**Enforcement**: N/A

---

### Per.10: Rely on the static type system

**Reason**: Type violations, weak types (e.g. `void*`s), and low-level code make the job of the optimizer much harder.

**Enforcement**: N/A

---

### Per.11: Move computation from run time to compile time

**Reason**: To decrease code size and run time. To avoid data races by using constants. To catch errors at compile time.

**Example**:
```cpp
double square(double d) { return d*d; }
static double s2 = square(2);    // old-style: dynamic initialization

constexpr double ntimes(double d, int n)   // assume 0 <= n
{
    double m = 1;
    while (n--) m *= d;
    return m;
}
constexpr double s3 {ntimes(2, 3)};  // modern-style: compile-time initialization
```

**Enforcement**:
- Look for simple functions that might be constexpr (but are not)
- Look for functions called with all constant-expression arguments

---

### Per.12: Eliminate redundant aliases

**Reason**: Redundant aliases can confuse optimizers and prevent beneficial transformations.

**Enforcement**: N/A

---

### Per.13: Eliminate redundant indirections

**Reason**: Redundant indirections add overhead and can prevent optimization opportunities.

**Enforcement**: N/A

---

### Per.14: Minimize the number of allocations and deallocations

**Reason**: Memory allocation and deallocation are expensive operations. Reducing them improves performance.

**Enforcement**: N/A

---

### Per.15: Do not allocate on a critical branch

**Reason**: Memory allocation is unpredictable and can cause latency spikes on performance-critical paths.

**Enforcement**: N/A

---

### Per.16: Use compact data structures

**Reason**: Performance is typically dominated by memory access times. Compact data structures improve cache utilization.

**Enforcement**: N/A

---

### Per.17: Declare the most used member of a time-critical struct first

**Reason**: Members declared first are at lower addresses, which can improve cache line utilization for frequently accessed members.

**Enforcement**: N/A

---

### Per.18: Space is time

**Reason**: Performance is typically dominated by memory access times. Smaller data structures are faster because they fit better in cache.

**Enforcement**: N/A

---

### Per.19: Access memory predictably

**Reason**: Performance is very sensitive to cache performance, and cache algorithms favor simple (usually linear) access to adjacent data.

**Example, bad**:
```cpp
int matrix[rows][cols];

for (int c = 0; c < cols; ++c)
    for (int r = 0; r < rows; ++r)
        sum += matrix[r][c];
```

**Example, good**:
```cpp
int matrix[rows][cols];

for (int r = 0; r < rows; ++r)
    for (int c = 0; c < cols; ++c)
        sum += matrix[r][c];
```

**Enforcement**: N/A

---

### Per.30: Avoid context switches on the critical path

**Reason**: Context switches (system calls, thread switches, etc.) are expensive and unpredictable, causing latency issues on performance-critical paths.

**Enforcement**: N/A

---

## CPL: C-style Programming

### CPL.1: Prefer C++ to C

**Reason**: C++ provides better type checking and more notational support. It provides better support for high-level programming and often generates faster code.

**Example, bad**:
```cpp
char ch = 7;
void* pv = &ch;
int* pi = pv;   // not C++
*pi = 999;      // overwrite sizeof(int) bytes near &ch
```

The rules for implicit casting to and from `void*` in C are subtle and unenforced.

**Enforcement**: Use a C++ compiler.

---

### CPL.2: If you must use C, use the common subset of C and C++, and compile the C code as C++

**Reason**: That subset can be compiled with both C and C++ compilers, and when compiled as C++ is better type checked than "pure C."

**Example**:
```cpp
int* p1 = malloc(10 * sizeof(int));                      // not C++
int* p2 = static_cast<int*>(malloc(10 * sizeof(int)));   // not C, C-style C++
int* p3 = new int[10];                                   // not C
int* p4 = (int*) malloc(10 * sizeof(int));               // both C and C++
```

**Enforcement**: Flag if using a build mode that compiles code as C.

---

### CPL.3: If you must use C for interfaces, use C++ in the calling code using such interfaces

**Reason**: C++ is more expressive than C and offers better support for many types of programming.

**Example**: For a 3rd party C library, define the low-level interface in the common subset of C and C++ for better type checking. Encapsulate the low-level interface in a C++ interface.

**Enforcement**: N/A

---

## A: Architectural Ideas

### A.1: Separate stable code from less stable code

**Reason**: Isolating less stable code facilitates its unit testing, interface improvement, refactoring, and eventual deprecation.

**Enforcement**: N/A

---

### A.2: Express potentially reusable parts as a library

**Reason**: A library is a collection of declarations and definitions maintained, documented, and shipped together. A library could be header-only or headers plus object files.

**Note**: Promoting reuse through libraries improves maintainability and reduces code duplication.

**Enforcement**: N/A

---

### A.4: There should be no cycles among libraries

**Reason**:
- A cycle complicates the build process
- Cycles are hard to understand and might introduce indeterminism

**Note**: A library can contain cyclic references in the definition of its components, but a library should not depend on another that depends on it.

**Enforcement**: N/A
