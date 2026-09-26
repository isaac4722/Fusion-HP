// ============================================================================
//  Fusion-HP · Logger — declaración (implementación en Logger.cpp)
// ============================================================================
#pragma once
#include "Common.h"

namespace fusion {

class Logger {
public:
    static void Init();
    static void SetLevel(int lv);          // 0=ERROR..3=DEBUG (live: mínimo INFO [SPEC §11.1])
    static void Error(const char* module, const std::string& msg);
    static void Warn(const char* module, const std::string& msg);
    static void Info(const char* module, const std::string& msg);
    static void Debug(const char* module, const std::string& msg);
    static void LogException(const char* module, const char* where, unsigned int code,
                             EXCEPTION_POINTERS* ep = nullptr);
private:
    static void Write(int lv, const char* module, const std::string& msg);
    static void RotateIfNeeded();
};

} // namespace fusion
