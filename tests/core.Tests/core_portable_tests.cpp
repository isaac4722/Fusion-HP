// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  core_portable_tests.cpp : portable test harness for the native core
//  (fixture gates F0.01-F0.05, F0.09, F1.01-adjacent, F6.01 — local g++ arm;
//  the MSVC/Win32 gates run on CI windows-latest against src/core/core.vcxproj).
//
//  Style: mirrors the managed harness (src/managed/Tests/Tests.cs) — selftest
//  WITHOUT external frameworks. Output: "CORE TESTS PASS n/n", exit 0 on
//  green, 1 on red. Assert messages and output are ENGLISH (code style rule);
//  user-facing data strings keep the es-VE contract of the core.
// ============================================================================
#include "Bootstrap.h"
#include "EnvironmentReport.h"
#include "IpcV1.h"
#include "NativeLog.h"

#include "BibleRef.h"
#include "BibleBib.h"
#include "Chords.h"
#include "Highlight.h"
#include "Lyrics.h"
#include "Scripture.h"
#include "SongModel.h"
#include "Storage.h"
#include "LuminaCore.h"

#include <nlohmann/json.hpp>

#include <chrono>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

namespace {

int  g_pass = 0;
int  g_fail = 0;
std::vector<std::string> g_failures;

void Check(bool cond, const char* name, const std::string& detail) {
    if (cond) {
        ++g_pass;
        std::printf("  [ OK ] %s\n", name);
    } else {
        ++g_fail;
        g_failures.push_back(std::string(name) + " (" + detail + ")");
        std::printf("  [FAIL] %s (%s)\n", name, detail.c_str());
    }
}

/* ------------------------------------------------- 1. ipc.v1 framing ---- */
void TestIpcRoundtrip() {
    using namespace lumina::ipc;
    const std::string payload = "{\"proto\":\"ipc.v1\",\"client\":\"tests\"}";
    const std::vector<uint8_t> fr = EncodeFrame(MSG_HELLO, payload);
    bool ok = fr.size() == kHeaderSize + payload.size();
    ok = ok && fr[0] == 0x50 && fr[1] == 0x49 && fr[2] == 0x4D && fr[3] == 0x4C; // LE magic "LMIP"
    ok = ok && fr[4] == 0x01 && fr[5] == 0x00;                                   // version 1
    ok = ok && fr[6] == (uint8_t)MSG_HELLO && fr[7] == 0x00;

    FrameDecoder dec;
    uint16_t type = 0; std::string out; size_t used = 0;
    const int rc = dec.Feed(fr.data(), fr.size(), &type, &out, &used);
    ok = ok && rc == 1 && type == MSG_HELLO && out == payload &&
         used == fr.size() && dec.Buffered() == 0;
    Check(ok, "ipc.v1: encode/decode roundtrip keeps magic+version+type",
          "rc=" + std::to_string(rc));
}

void TestIpcFragmentation() {
    using namespace lumina::ipc;
    const std::string p1 = "{\"id\":1,\"action\":\"next\"}";
    const std::string p2 = "{\"id\":2,\"action\":\"prev\"}";
    std::vector<uint8_t> stream = EncodeFrame(MSG_COMMAND, p1);
    const std::vector<uint8_t> f2 = EncodeFrame(MSG_COMMAND, p2);
    stream.insert(stream.end(), f2.begin(), f2.end());

    FrameDecoder dec;
    bool ok = true;
    int frames = 0;
    std::string got1, got2;
    // Feed byte by byte (F0.05.9 fragmentation).
    for (size_t i = 0; i < stream.size(); ++i) {
        uint16_t t = 0; std::string pl; size_t used = 0;
        const int rc = dec.Feed(&stream[i], 1, &t, &pl, &used);
        if (rc == 1) {
            ++frames;
            if (frames == 1) got1 = pl;
            if (frames == 2) got2 = pl;
            // Frame completion consumed the WHOLE frame from the decoder
            // buffer (header 12 + payload) — same contract as the
            // whole-frame roundtrip test above.
            ok = ok && used == (12 + (frames == 1 ? p1.size() : p2.size()));
        } else {
            ok = ok && rc == 0 && used == 0;
        }
    }
    ok = ok && frames == 2 && got1 == p1 && got2 == p2;
    Check(ok, "ipc.v1: byte-by-byte fragmentation reassembles two frames",
          "frames=" + std::to_string(frames));
}

void TestIpcProtocolErrors() {
    using namespace lumina::ipc;
    const std::string payload = "x";
    std::vector<uint8_t> fr = EncodeFrame(MSG_PING, payload);

    // Bad magic: session survives, decoder resets (F0.05.7).
    std::vector<uint8_t> bad = fr;
    bad[0] = 'X';
    FrameDecoder d1;
    uint16_t t = 0; std::string pl; size_t used = 0;
    bool ok = d1.Feed(bad.data(), bad.size(), &t, &pl, &used) == -1;

    // Unknown version.
    std::vector<uint8_t> ver = fr;
    ver[4] = 0x63;
    ok = ok && d1.Feed(ver.data(), ver.size(), &t, &pl, &used) == -2;

    // Oversized payload vs maxPayload.
    const std::string big(64, 'z');
    std::vector<uint8_t> bigfr = EncodeFrame(MSG_STATE, big);
    FrameDecoder small(32);   // maxPayload = 32
    ok = ok && small.Feed(bigfr.data(), bigfr.size(), &t, &pl, &used) == -3;

    // After errors the decoder still decodes a valid frame.
    ok = ok && d1.Feed(fr.data(), fr.size(), &t, &pl, &used) == 1 && pl == payload;
    Check(ok, "ipc.v1: invalid magic/unknown version/oversized rejected without crash",
          "protocol error handling");
}

void TestIpcCommandQueue() {
    using namespace lumina::ipc;
    // Capacity + FIFO + metrics (backpressure, F0.05.5).
    CommandQueue::Options o;
    o.maxQueued = 2;
    o.maxAgeMs  = 100000;
    CommandQueue q(o);
    bool ok = q.TryPush(1, "next", "") && q.TryPush(2, "prev", "");
    ok = ok && !q.TryPush(3, "black", "");              // full → reject, no block
    CommandQueue::Stats st = q.StatsSnapshot();
    ok = ok && st.enqueued == 2 && st.rejectedFull == 1 && q.Depth() == 2;
    CommandQueue::Item it;
    ok = ok && q.TryPop(&it) && it.id == 1 && it.action == "next";
    ok = ok && q.TryPop(&it) && it.id == 2;
    ok = ok && !q.TryPop(&it);
    st = q.StatsSnapshot();
    ok = ok && st.executed == 2;

    // Expiration: maxAgeMs=-1 makes every item older than the deadline
    // (deterministic, no sleeps) → dropped and counted (F6.01).
    CommandQueue::Options ex;
    ex.maxQueued = 8;
    ex.maxAgeMs  = -1;
    CommandQueue q2(ex);
    q2.TryPush(7, "next", "");
    q2.TryPush(8, "next", "");
    ok = ok && !q2.TryPop(&it);
    st = q2.StatsSnapshot();
    ok = ok && st.expiredDropped == 2 && st.executed == 0;

    // Live latency boundary: fresh item within maxAgeMs is delivered.
    CommandQueue::Options live;
    live.maxQueued = 8;
    live.maxAgeMs  = 50;
    CommandQueue q3(live);
    q3.TryPush(9, "line_next", "");
    std::this_thread::sleep_for(std::chrono::milliseconds(5));
    ok = ok && q3.TryPop(&it) && it.id == 9;
    Check(ok, "ipc.v1: CommandQueue capacity/expiration/metrics (non-blocking)",
          "stats enq=" + std::to_string(st.enqueued));
}

/* -------------------------------------- 2. profile classification ------ */
void TestProfileClassification() {
    using namespace lumina::bootstrap;
    // Table of §4.2: A = 4.7.2+ (release >= 461808); 4.7.1 = 461310 stays B.
    DotnetFacts a{}; a.v4Full = true; a.v4Release = 528040;   // 4.8
    DotnetFacts a2{}; a2.v4Full = true; a2.v4Release = 461808; // 4.7.2
    DotnetFacts b1{}; b1.v4Full = true; b1.v4Release = 461310; // 4.7.1 → B
    DotnetFacts b2{}; b2.net35 = true;                         // 3.5 SP1 → B
    DotnetFacts b3{}; b3.v4Full = true; b3.v4Release = 394802; // 4.6.2 → B
    DotnetFacts c{};                                            // nothing → C
    bool ok = ClassifyProfile(a) == RuntimeProfile::A &&
              ClassifyProfile(a2) == RuntimeProfile::A &&
              ClassifyProfile(b1) == RuntimeProfile::B &&
              ClassifyProfile(b2) == RuntimeProfile::B &&
              ClassifyProfile(b3) == RuntimeProfile::B &&
              ClassifyProfile(c) == RuntimeProfile::C;
    ok = ok && std::string(RuntimeProfileLabel(RuntimeProfile::A)) == "A" &&
              std::string(DotnetReleaseLabel(a)) == "4.8+";
    Check(ok, "bootstrap F0.03: profile classification A/B/C (synthetic NDP)",
          "thresholds 461808 / 3.5 / none");
}

void TestOsClassification() {
    using namespace lumina::bootstrap;
    bool ok = ClassifyOs(6, 1, 7601, true) == OsClass::Win7Sp1 &&
              ClassifyOs(6, 1, 7600, false) == OsClass::Win7NoSp1 &&
              ClassifyOs(6, 2, 9200, false) == OsClass::Win8 &&
              ClassifyOs(6, 3, 9600, false) == OsClass::Win81 &&
              ClassifyOs(10, 0, 19045, false) == OsClass::Win10 &&
              ClassifyOs(10, 0, 22631, false) == OsClass::Win11 &&
              ClassifyOs(5, 1, 2600, false) == OsClass::Older;
    ok = ok && OsSupported(OsClass::Win7Sp1) && !OsSupported(OsClass::Win7NoSp1) &&
              !OsSupported(OsClass::Older) && OsSupported(OsClass::Win11);
    ok = ok && std::string(OsClassLabel(OsClass::Win11)) == "win11";
    ArchFacts f{}; f.processBitness = 64; f.osBitness = 64;
    ok = ok && ChooseArch(f, false, false) == ArchChoice::X64;
    f.processBitness = 32; f.wow64 = true;
    ok = ok && ChooseArch(f, false, false) == ArchChoice::X86;
    ok = ok && ChooseArch(f, true, false) == ArchChoice::X86 &&
              ChooseArch(f, false, true) == ArchChoice::X64;   // explicit wins
    Check(ok, "bootstrap F0.01/F0.02: OS classification + arch policy",
          "win7sp1/8/8.1/10/11 + x86/x64/force");
}

/* ----------------------------------------- 3. ahp.v1 JSON (minimum) ---- */
void TestAhpJsonParse() {
    using namespace lumina;
    const json song = json::parse(
        "{\"id\":\"s1\",\"title\":\"Canción de prueba\","
        "\"lyrics\":\"[Verso 1]\\nPrimera línea\\nSegunda línea\\n\\n"
        "[Coro]\\nAleluya\\n\",\"maxLinesPerSlide\":2}");
    json res = SongModel::ParseAndBuildSlides(song);
    bool ok = res.contains("ok") && res["ok"] == 1;
    ok = ok && res.contains("slides") && res["slides"].is_array() &&
         res["slides"].size() >= 2;                     // 2 lines/slide + chorus
    ok = ok && res["song"]["title"] == "Canción de prueba";
    Check(ok, "ahp.v1: minimal JSON parse builds slides (SongModel)",
          "ok=1, slides>=2");
}

/* ------------------------------------------------- 4. NativeLog F0.09 -- */
void TestNativeLog() {
    using namespace lumina::nlog;
    const std::string dir = "logs-core-tests";
    Log log;
    Log::Options o;
    o.dir = dir;
    o.minLevel = SEV_INFO;
    bool ok = log.Open(o) && log.IsOpen();
    ok = ok && log.Write(SEV_INFO, "render", "renderer ready (selftest)");
    ok = ok && log.Write(SEV_WARN, "ipc", "command expired");
    ok = ok && log.Write(SEV_DEBUG, "test", "dropped below level");
    Log::Stats st = log.StatsSnapshot();
    ok = ok && st.written == 2 && st.dropped == 1;      // DEBUG below INFO
    log.SetLevel(SEV_DEBUG);
    ok = ok && log.Write(SEV_DEBUG, "test", "debug enabled");
    ok = ok && log.StatsSnapshot().written == 3;

    // §10.1 fields on disk: ts_utc | ts_local | severity | module | message.
    const std::string path = log.ActiveFile();
    ok = ok && path.find("lumina-") != std::string::npos &&
              path.find(".log") != std::string::npos;
    std::ifstream f(path.c_str());
    std::stringstream buf;
    buf << f.rdbuf();
    const std::string all = buf.str();
    ok = ok && all.find("|INFO|render|") != std::string::npos;
    ok = ok && all.find("|WARN|ipc|") != std::string::npos;
    ok = ok && all.find("|DEBUG|test|") != std::string::npos;
    // Timestamps: UTC line ends with 'Z' before the separator; local has offset.
    ok = ok && all.size() > 40 && all[4] == '-' && all[10] == 'T';
    ok = ok && all.find("|ERROR|") == std::string::npos;   // nothing wrote ERROR

    // §10.4 redaction of credentials.
    const std::string red = Redact("Authorization: Bearer abc123 token=zz");
    ok = ok && red.find("abc123") == std::string::npos &&
              red.find("[REDACTED]") != std::string::npos;

    log.Close();
    ok = ok && !log.IsOpen();
    Check(ok, "nlog F0.09: daily file, severities, ts fields, redaction, stats",
          "dir=" + dir);
}

/* --------------------------------------- 5. Scripture / BibleRef ------- */
void TestScriptureBibleRef() {
    using namespace lumina;
    const BibleRef::VerseRef r = BibleRef::Resolve("Jn 3:16");
    bool ok = r.Valid() && r.book == 43 && r.chapter == 3 && r.verse == 16 &&
              r.bookName == "Juan";
    ok = ok && BibleRef::Resolve("no-existe 9:9").Valid() == false;

    std::vector<Slide> slides;
    ok = ok && Scripture::BuildSlides("Juan 3:16",
                                      {"Porque de tal manera amó Dios",
                                       "que ha dado a su Hijo unigénito"},
                                      2, &slides);
    ok = ok && slides.size() == 1 && slides[0].kind == SLIDE_SCRIPTURE &&
              slides[0].refLabel == "Juan 3:16-17" &&
              slides[0].lines.size() == 2;

    // Grouping: 4 verses, 2 per slide → 2 slides.
    std::vector<Slide> grouped;
    ok = ok && Scripture::BuildSlides("Jn 3:16", {"v1", "v2", "v3", "v4"}, 2,
                                      &grouped) && grouped.size() == 2 &&
              grouped[1].refLabel == "Juan 3:18-19";

    ok = ok && !Scripture::BuildSlides("zzz 1:1", {"x"}, 1, &slides);
    Check(ok, "scripture: Jn 3:16 resolves (book 43) and builds ref-labeled slides",
          "book=43 chapter=3 verse=16");
}

/* ------------------------------------------------- 6. Highlight -------- */
void TestHighlight() {
    using namespace lumina;
    // Accent/case-insensitive full-word match (Latin fold).
    std::vector<HlSegment> seg = Highlight::Split("Glória al DIOS alto", "dios");
    bool ok = seg.size() == 3 && seg[1].match && seg[1].text == "DIOS" &&
              !seg[0].match && !seg[2].match;
    // Word boundary: "Diosas" must NOT match "Dios".
    seg = Highlight::Split("Diosas y Dios", "Dios");
    bool matched = false;
    for (const HlSegment& s : seg) matched = matched || (s.match && s.text == "Dios");
    ok = ok && matched;
    for (const HlSegment& s : seg) {
        if (s.match && s.text.find("Diosas") != std::string::npos) ok = false;
    }
    Check(ok, "highlight: accent/case-insensitive with word boundaries",
          "Glória/DIOS fold, Diosas excluded");
}

/* ------------------------------------------------- 7. Chords ----------- */
void TestChords() {
    using namespace lumina;
    bool ok = Chords::TransposeLine("Do Re Mi", 2, true) == "Re Mi Fa#";
    ok = ok && Chords::TransposeLine("C E G", 2, false) == "D F# A";
    ok = ok && Chords::IsChordLine("Do Sol Do") &&
              !Chords::IsChordLine("el Señor es mi pastor");
    Check(ok, "chords: transpose latin/anglo lines and chord-line detection",
          "Do Re Mi +2 -> Re Mi Fa#");
}

/* ------------------------------------- 8. Storage SQLite FTS5 ---------- */
void TestStorageFts() {
    using namespace lumina;
    Database db;
    std::string err;
    const std::string dir = "logs-core-tests";
    const std::string path = dir + "/core-tests.db";
    // The harness binary runs with cwd = tests/core.Tests/build.
    (void)std::system(("mkdir -p '" + dir + "'").c_str());
    std::remove(path.c_str());
    bool ok = db.Open(path, &err);
    if (!ok) {
        Check(false, "storage: SQLite FTS5 open+schema", err);
        return;
    }
    auto exec = [&](const std::string& sql, const std::vector<std::string>& params,
                    std::string* out) {
        json req;
        req["sql"] = sql;
        req["params"] = params;
        return db.ExecJson(req.dump(), out, &err);
    };
    std::string out;
    ok = exec("INSERT INTO songs(title, author, lyrics, tags) VALUES(?,?,?,?)",
              {"Granito de fe", "Autor de prueba", "aleluya granito semilla", "prueba"},
              &out);
    json res = ok ? json::parse(out) : json();
    ok = ok && res["changes"] == 1 && res["lastId"] > 0;

    // FTS5 MATCH over the mirrored index (bible_fts too).
    ok = exec("SELECT title, lyrics FROM songs_fts WHERE songs_fts MATCH ?",
              {"aleluya"}, &out);
    res = ok ? json::parse(out) : json();
    ok = ok && res["rows"].size() == 1 && res["rows"][0][0] == "Granito de fe";

    ok = exec("SELECT count(*) FROM songs_fts WHERE songs_fts MATCH ?",
              {"palabrainexistente"}, &out);
    res = ok ? json::parse(out) : json();
    ok = ok && res["rows"][0][0] == 0;

    // Plain row roundtrip with bound params (no interpolation).
    ok = exec("SELECT author FROM songs WHERE title = ?", {"Granito de fe"}, &out);
    res = ok ? json::parse(out) : json();
    ok = ok && res["rows"][0][0] == "Autor de prueba";
    Check(ok, "storage: SQLite FTS5 insert/select/MATCH with bound params",
          ok ? "rows ok" : err);
}

/* --------------------------------- 9. EnvironmentReport F0.09/F6.01 ---- */
void TestEnvironmentReport() {
    using namespace lumina;
    // Injected facts (portable pure path): Win11 x64 with .NET 4.8 → profile A.
    const std::string probe =
        "{\"osMajor\":10,\"osMinor\":0,\"osBuild\":22631,\"osSp1\":true,"
        "\"procBitness\":64,\"osBitness\":64,\"wow64\":false,\"hasWow2\":true,"
        "\"net35\":true,\"net4Full\":true,\"net4Release\":528040}";
    const EnvironmentReport rep =
        MakeEnvironmentReport(probe, false, false, false, false, false);
    json j;
    bool ok = true;
    try {
        j = json::parse(rep.ToJson());
    } catch (...) { ok = false; }
    ok = ok && j.contains("os") && j["os"].contains("label") &&
              j["os"].contains("class") && j["os"]["class"] == "win11";
    ok = ok && j.contains("net") && j["net"].contains("profile") &&
              j["net"]["profile"] == "A" && j["net"]["version"] == "4.8+";
    ok = ok && j["arch"]["process"] == "x64" && j["arch"]["machine"] == "x64";
    ok = ok && j.contains("startedAtUtc") &&
              rep.startedAtUtc.size() == 20 && rep.startedAtUtc[10] == 'T' &&
              rep.startedAtUtc[19] == 'Z';
    ok = ok && rep.bootDecision.find("perfil A") != std::string::npos;

    // Default (no probe): the harness platform reports itself; profile is
    // empty on non-Windows ("n/a") and the label names the OS family.
    const EnvironmentReport self = MakeEnvironmentReport("", false, false,
                                                         false, false, false);
    ok = ok && (self.osLabel.find("Windows") == 0 || self.osLabel.find("Linux") == 0);
    ok = ok && !self.startedAtUtc.empty();
    Check(ok, "environment F0.09/F6.01: ToJson carries osLabel/profile/bootDecision",
          "probe win11 -> profile A");
}

} // namespace

int main() {
    std::printf("Lumina core portable harness (g++ subset, no GUI)\n");
    TestIpcRoundtrip();
    TestIpcFragmentation();
    TestIpcProtocolErrors();
    TestIpcCommandQueue();
    TestProfileClassification();
    TestOsClassification();
    TestAhpJsonParse();
    TestNativeLog();
    TestScriptureBibleRef();
    TestHighlight();
    TestChords();
    TestStorageFts();
    TestEnvironmentReport();

    const int total = g_pass + g_fail;
    if (g_fail == 0) {
        std::printf("CORE TESTS PASS %d/%d\n", g_pass, total);
        return 0;
    }
    std::printf("CORE TESTS FAIL %d/%d detail:\n", g_fail, total);
    for (const std::string& f : g_failures) std::printf("  - %s\n", f.c_str());
    return 1;
}
