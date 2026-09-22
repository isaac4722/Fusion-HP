// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Utf8.h : utilidades UTF-8 portables (sin wx, sin locale del sistema).
// ============================================================================
#ifndef LUMINA_UTF8_H
#define LUMINA_UTF8_H

#include <string>
#include <vector>
#include <cstdint>

namespace lumina {

// Recorta espacios ASCII laterales.
inline std::string Trim(const std::string& s) {
    size_t a = 0, b = s.size();
    while (a < b && (unsigned char)s[a] <= ' ') ++a;
    while (b > a && (unsigned char)s[b - 1] <= ' ') --b;
    return s.substr(a, b - a);
}

// Minúsculas ASCII (suficiente para etiquetas/comandos; texto de usuario intacto).
inline std::string ToLowerAscii(const std::string& s) {
    std::string o = s;
    for (size_t i = 0; i < o.size(); ++i)
        if (o[i] >= 'A' && o[i] <= 'Z') o[i] = (char)(o[i] - 'A' + 'a');
    return o;
}

// ¿Empieza con 'prefix' (ASCII case-insensitive)?
inline bool StartsWithCI(const std::string& s, const std::string& prefix) {
    if (s.size() < prefix.size()) return false;
    return ToLowerAscii(s.substr(0, prefix.size())) == ToLowerAscii(prefix);
}

// Divide por un separador de un carácter.
inline std::vector<std::string> Split(const std::string& s, char sep) {
    std::vector<std::string> out;
    std::string cur;
    for (size_t i = 0; i <= s.size(); ++i) {
        if (i == s.size() || s[i] == sep) { out.push_back(cur); cur.clear(); }
        else cur += s[i];
    }
    return out;
}

// Reemplazo simple.
inline void ReplaceAll(std::string& s, const std::string& from, const std::string& to) {
    if (from.empty()) return;
    size_t pos = 0;
    while ((pos = s.find(from, pos)) != std::string::npos) {
        s.replace(pos, from.size(), to);
        pos += to.size();
    }
}

// ¿Empieza por un prefijo byte a byte?
inline bool StartsWith(const std::string& s, const std::string& prefix) {
    return s.size() >= prefix.size() && s.compare(0, prefix.size(), prefix) == 0;
}

// ¿Termina con un sufijo byte a byte?
inline bool EndsWith(const std::string& s, const std::string& suffix) {
    return s.size() >= suffix.size() && s.compare(s.size() - suffix.size(), suffix.size(), suffix) == 0;
}

// Decode de un codepoint UTF-8; avanza i. Devuelve U+FFFD si es inválido.
inline uint32_t DecodeUtf8(const std::string& s, size_t& i) {
    const unsigned char c = (unsigned char)s[i];
    if (c < 0x80) { ++i; return c; }
    int n = 0; uint32_t cp = 0;
    if ((c & 0xE0) == 0xC0) { n = 1; cp = c & 0x1F; }
    else if ((c & 0xF0) == 0xE0) { n = 2; cp = c & 0x0F; }
    else if ((c & 0xF8) == 0xF0) { n = 3; cp = c & 0x07; }
    else { ++i; return 0xFFFD; }
    if ((int)s.size() - (int)i < n + 1) { ++i; return 0xFFFD; }
    for (int k = 1; k <= n; ++k) {
        const unsigned char cc = (unsigned char)s[i + k];
        if ((cc & 0xC0) != 0x80) { ++i; return 0xFFFD; }
        cp = (cp << 6) | (cc & 0x3F);
    }
    i += n + 1;
    return cp;
}

// Elimina acentos/diacríticos españoles (mapeo explícito, determinista —
// igual semántica que BibleRef::StripAccents de la edición wx v2.0.0).
inline std::string StripAccents(const std::string& utf8) {
    static const uint32_t kMap[][2] = {
        {0xE1,'a'},{0xE0,'a'},{0xE4,'a'},{0xE2,'a'},{0xE3,'a'},
        {0xE9,'e'},{0xE8,'e'},{0xEB,'e'},{0xEA,'e'},
        {0xED,'i'},{0xEC,'i'},{0xEF,'i'},{0xEE,'i'},
        {0xF3,'o'},{0xF2,'o'},{0xF6,'o'},{0xF4,'o'},{0xF5,'o'},
        {0xFA,'u'},{0xF9,'u'},{0xFC,'u'},{0xFB,'u'},
        {0xF1,'n'},{0xE7,'c'},
        {0xC1,'A'},{0xC0,'A'},{0xC4,'A'},{0xC2,'A'},{0xC3,'A'},
        {0xC9,'E'},{0xC8,'E'},{0xCB,'E'},{0xCA,'E'},
        {0xCD,'I'},{0xCC,'I'},{0xCF,'I'},{0xCE,'I'},
        {0xD3,'O'},{0xD2,'O'},{0xD6,'O'},{0xD4,'O'},{0xD5,'O'},
        {0xDA,'U'},{0xD9,'U'},{0xDC,'U'},{0xDB,'U'},
        {0xD1,'N'},{0xC7,'C'},
    };
    std::string out;
    out.reserve(utf8.size());
    size_t i = 0;
    while (i < utf8.size()) {
        const uint32_t cp = DecodeUtf8(utf8, i);
        if (cp < 0x80) { out += (char)cp; continue; }
        bool mapped = false;
        for (const auto& m : kMap) {
            if (m[0] == cp) { out += (char)m[1]; mapped = true; break; }
        }
        if (!mapped) {
            if (cp < 0x800) {
                out += (char)(0xC0 | (cp >> 6));
                out += (char)(0x80 | (cp & 0x3F));
            } else if (cp < 0x10000) {
                out += (char)(0xE0 | (cp >> 12));
                out += (char)(0x80 | ((cp >> 6) & 0x3F));
                out += (char)(0x80 | (cp & 0x3F));
            } else {
                out += (char)(0xF0 | (cp >> 18));
                out += (char)(0x80 | ((cp >> 12) & 0x3F));
                out += (char)(0x80 | ((cp >> 6) & 0x3F));
                out += (char)(0x80 | (cp & 0x3F));
            }
        }
    }
    return out;
}

// Recorta + colapsa espacios internos (semántica Simplified de Qt).
inline std::string Simplified(const std::string& s) {
    std::string t = Trim(s), o;
    bool lastSpace = false;
    for (size_t i = 0; i < t.size(); ++i) {
        if ((unsigned char)t[i] <= ' ') {
            if (!lastSpace) { o += ' '; lastSpace = true; }
        } else { o += t[i]; lastSpace = false; }
    }
    return o;
}

#ifdef _WIN32
// Solo Windows: UTF-8 <-> UTF-16 (para APIs Win32). El núcleo portable no
// depende de conversiones del SO.
std::wstring Utf8ToWide(const std::string& s);
std::string  WideToUtf8(const std::wstring& w);
#endif

} // namespace lumina
#endif // LUMINA_UTF8_H
