// ============================================================================
//  Fusion-HP · core/Highlight.h — resaltado de palabras en la PROYECCIÓN
// (función única heredada de las betas 1 «Lumina», port de
// native/core/src/Highlight.{h,cpp} a wchar_t). Parte una línea en segmentos
// marcando las ocurrencias de las palabras buscadas con coincidencia
// INSENSIBLE a mayúsculas y a acentos latinos (á→a, É→e, ü→u, ñ→n…) y con
// frontera de palabra (no marca «Dios» dentro de «Diosas»). El texto de cada
// segmento se conserva VERBATIM: solo cambia el pincel con que se dibuja.
// ============================================================================
#pragma once
#include <string>
#include <vector>
#include <cwctype>

namespace fusion {

struct HlSegment {
    std::wstring text;
    bool match = false;
    HlSegment() {}
    HlSegment(const std::wstring& t, bool m) : text(t), match(m) {}
};

class Highlight {
public:
    /// <summary>Segmenta marcando las ocurrencias de <paramref name="words"/>.</summary>
    static std::vector<HlSegment> Split(const std::wstring& text,
                                        const std::vector<std::wstring>& words) {
        std::vector<HlSegment> out;
        if (words.empty()) {
            out.push_back(HlSegment(text, false));
            return out;
        }
        // Plegar palabras válidas
        std::vector<std::wstring> folded;
        for (auto& w : words) {
            std::wstring fw = FoldTrim(w);
            if (!fw.empty()) folded.push_back(fw);
        }
        if (folded.empty()) {
            out.push_back(HlSegment(text, false));
            return out;
        }

        std::vector<wchar_t> fold(text.size());
        for (size_t i = 0; i < text.size(); i++) fold[i] = Fold(text[i]);
        std::vector<bool> marks(text.size(), false);

        for (size_t i = 0; i < text.size(); i++) {
            if (i > 0 && IsWordChar(text[i - 1])) continue;      // frontera izquierda
            for (auto& fw : folded) {
                if (i + fw.size() > text.size()) continue;
                bool hit = true;
                for (size_t k = 0; k < fw.size(); k++)
                    if (fold[i + k] != fw[k]) { hit = false; break; }
                if (!hit) continue;
                size_t end = i + fw.size();
                if (end < text.size() && IsWordChar(text[end])) continue;  // frontera derecha
                for (size_t k = i; k < end; k++) marks[k] = true;
                break;
            }
        }

        std::wstring buf;
        bool cur = false;
        for (size_t i = 0; i < text.size(); i++) {
            if (i == 0) {
                cur = marks[0];
                buf.push_back(text[i]);
                continue;
            }
            if (marks[i] == cur) { buf.push_back(text[i]); continue; }
            out.push_back(HlSegment(buf, cur));
            buf.clear();
            cur = marks[i];
            buf.push_back(text[i]);
        }
        if (!buf.empty()) out.push_back(HlSegment(buf, cur));
        if (out.empty()) out.push_back(HlSegment(text, false));
        return out;
    }

private:
    static wchar_t Fold(wchar_t c) {
        switch (c) {
            case L'A': case L'Á': case L'À': case L'Â': case L'Ã': case L'Ä': case L'Å': return L'a';
            case L'a': case L'á': case L'à': case L'â': case L'ã': case L'ä': case L'å': return L'a';
            case L'C': case L'Ç': return L'c';
            case L'c': case L'ç': return L'c';
            case L'E': case L'É': case L'È': case L'Ê': case L'Ë': return L'e';
            case L'e': case L'é': case L'è': case L'ê': case L'ë': return L'e';
            case L'I': case L'Í': case L'Ì': case L'Î': case L'Ï': return L'i';
            case L'i': case L'í': case L'ì': case L'î': case L'ï': return L'i';
            case L'N': case L'Ñ': return L'n';
            case L'n': case L'ñ': return L'n';
            case L'O': case L'Ó': case L'Ò': case L'Ô': case L'Õ': case L'Ö': return L'o';
            case L'o': case L'ó': case L'ò': case L'ô': case L'õ': case L'ö': return L'o';
            case L'U': case L'Ú': case L'Ù': case L'Û': case L'Ü': return L'u';
            case L'u': case L'ú': case L'ù': case L'û': case L'ü': return L'u';
            case L'Y': case L'Ý': case L'Ÿ': return L'y';
            case L'y': case L'ý': case L'ÿ': return L'y';
            default:
                if (c >= L'A' && c <= L'Z') return (wchar_t)(c - L'A' + L'a');
                return c;
        }
    }

    static std::wstring FoldTrim(const std::wstring& s) {
        std::wstring r;
        size_t a = 0, b = s.size();
        while (a < b && iswspace((wint_t)s[a])) a++;
        while (b > a && iswspace((wint_t)s[b - 1])) b--;
        for (size_t i = a; i < b; i++) r.push_back(Fold(s[i]));
        return r;
    }

    static bool IsWordChar(wchar_t c) {
        wchar_t f = Fold(c);
        return (f >= L'a' && f <= L'z') || (f >= L'0' && f <= L'9');
    }
};

} // namespace fusion
