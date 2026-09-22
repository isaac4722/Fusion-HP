// Contrato: tabla canónica 66 libros + resolución de referencias.
#ifndef FUSION_BIBLEREF_H
#define FUSION_BIBLEREF_H

#include <string>
#include <vector>

namespace fusion {
class BibleRef {
public:
    struct BookInfo { int number; std::string name; std::vector<std::string> abbrs; };
    struct VerseRef { int book=0, chapter=0, verse=0; std::string bookName;
                      bool Valid() const { return book>0 && chapter>0; } };

    static const std::vector<BookInfo>& Books();
    static VerseRef Resolve(const std::string& input);   // "Jn 3:16", "1 co 13,4-7"…
    static std::string FormatRef(const VerseRef& r);
};
} // namespace fusion
#endif
