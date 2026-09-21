// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  BibleRef.h : Tabla canonica de los 66 libros (nombres y abreviaturas en
//  espanol) y parser de referencias tipadas ("Jn 3:16", "salmo 23:1-6",
//  "1 co 13, 4-7"). Pensado para resolucion < 2 segundos sin red.
// ============================================================================
#ifndef LUMINA_BIBLEREF_H
#define LUMINA_BIBLEREF_H

#include <QString>
#include <QStringList>
#include <QVector>
#include <QRegularExpression>
#include <QPair>

class BibleRef
{
public:
    struct BookInfo
    {
        int number;         // 1..66 canonico
        QString name;       // nombre completo
        QStringList abbrs;  // abreviaturas aceptadas
    };

    struct VerseRef
    {
        int book = 0, chapter = 0, verse = 0;
        QString bookName;
        bool valid() const { return book > 0 && chapter > 0; }
    };

    struct Verse
    {
        VerseRef ref;
        QString version;
        QString text;
    };

    static const QVector<BookInfo> &books()
    {
        static const QVector<BookInfo> table = buildTable();
        return table;
    }

    static QString stripAccents(const QString &s)
    {
        // CORRECCION: la version anterior normalizaba a NFD (lo que DESCOMPONE
        // 'e' + acento en dos code points) y luego hacia replace() de
        // caracteres PREcompuestos, que ya no existen en la cadena. Resultado:
        // los acentos nunca se eliminaban y libros como "Génesis" o "Éxodo"
        // fallaban en el prefijo. Solucion: NFD + filtrado de marcas
        // combinantes (categoria Mark_NonSpacing), que cubre todos los
        // diacriticos del espanol.
        QString out = s.normalized(QString::NormalizationForm_D);
        QString result;
        result.reserve(out.size());
        for (const QChar &c : out) {
            if (c.category() != QChar::Mark_NonSpacing)
                result += c;
        }
        return result;
    }

    // Resuelve una referencia tipada. Acepta:
    //   "Jn 3:16"  "juan 3:16-18"  "salmo 23"  "1 co 13:4-7"  "mt 5,6-7"  "genesis 1"
    static VerseRef resolve(const QString &input)
    {
        VerseRef out;
        QString s = stripAccents(input).simplified().toLower();
        if (s.isEmpty()) return out;
        // Separa la parte del libro del resto (capitulo / versiculo)
        static const QRegularExpression splitRe(QStringLiteral("^([1-3]?\\s*[a-z]+\\.?(?:\\s+de\\s+[a-z]+\\.?)?)\\s+([0-9].*)?$"));
        QRegularExpressionMatch m = splitRe.match(s);
        QString bookPart = s;
        QString rest;
        if (m.hasMatch()) {
            bookPart = m.captured(1).simplified();
            rest = m.captured(2).isNull() ? QString() : m.captured(2);
        }
        bookPart = bookPart.replace(QChar('.'), QString()).simplified();

        const QVector<BookInfo> &table = books();
        int bookNum = 0;
        QString bookName;
        // 1) coincidencia exacta de abreviatura
        for (const BookInfo &b : table) {
            for (const QString &a : b.abbrs)
                if (a == bookPart) { bookNum = b.number; bookName = b.name; break; }
            if (bookNum) break;
        }
        // 2) prefijo unico de nombre completo
        if (!bookNum) {
            const QString bp = stripAccents(bookPart).remove(QChar(' '));
            for (const BookInfo &b : table) {
                const QString nm = stripAccents(b.name.toLower()).remove(QChar(' '));
                if (nm.startsWith(bp)) { bookNum = b.number; bookName = b.name; break; }
            }
        }
        if (!bookNum) return out;

        out.book = bookNum;
        out.bookName = bookName;
        if (!rest.isEmpty()) {
            rest.replace(QChar(','), QChar(':'));
            static const QRegularExpression nums(QStringLiteral("^(\\d+)(?::(\\d+))?(?:\\s*-\\s*(\\d+))?(?::(\\d+))?$"));
            QRegularExpressionMatch mr = nums.match(rest);
            if (mr.hasMatch()) {
                out.chapter = mr.captured(1).toInt();
                if (mr.captured(4).isEmpty())
                    out.verse = mr.captured(2).isEmpty() ? 0 : mr.captured(2).toInt();
                else
                    out.verse = mr.captured(4).toInt();
            } else {
                out.chapter = 0;
            }
        }
        return out;
    }

    // Parsea "libro cap:v1-v2" para rangos; devuelve (chapter, vFrom, vTo)
    static QPair<int, QPair<int, int>> rangeOf(const QString &input)
    {
        QString s = stripAccents(input).simplified().toLower();
        int chapter = 0, from = 0, to = 0;
        static const QRegularExpression re(QStringLiteral("(\\d+)\\s*:\\s*(\\d+)(?:\\s*-\\s*(\\d+))?"));
        QRegularExpressionMatch m = re.match(s);
        if (m.hasMatch()) {
            chapter = m.captured(1).toInt();
            from = m.captured(2).toInt();
            to = m.captured(3).isEmpty() ? from : m.captured(3).toInt();
            if (to < from) qSwap(from, to);
        }
        return { chapter, { from, to } };
    }

    static QString formatRef(const VerseRef &r)
    {
        if (!r.valid()) return QString();
        if (r.verse > 0) return QStringLiteral("%1 %2:%3").arg(r.bookName).arg(r.chapter).arg(r.verse);
        return QStringLiteral("%1 %2").arg(r.bookName).arg(r.chapter);
    }

private:
    static void addBook(QVector<BookInfo> &t, int n, const QString &name,
                        std::initializer_list<QString> abbrs)
    {
        BookInfo b; b.number = n; b.name = name;
        for (const QString &a : abbrs) b.abbrs << a.toLower();
        t.append(b);
    }

    static QVector<BookInfo> buildTable()
    {
        QVector<BookInfo> t;
        using S = QString;
        addBook(t, 1,  S("Génesis"),        { S("gn"), S("gen"), S("gé") });
        addBook(t, 2,  S("Éxodo"),          { S("ex"), S("exo"), S("éx") });
        addBook(t, 3,  S("Levítico"),       { S("lv"), S("lev") });
        addBook(t, 4,  S("Números"),        { S("nm"), S("num") });
        addBook(t, 5,  S("Deuteronomio"),   { S("dt"), S("deu"), S("deut") });
        addBook(t, 6,  S("Josué"),          { S("jos"), S("josu") });
        addBook(t, 7,  S("Jueces"),         { S("jue"), S("jueces") });
        addBook(t, 8,  S("Rut"),            { S("rt"), S("rut") });
        addBook(t, 9,  S("1 Samuel"),       { S("1s"), S("1sa"), S("1 sam") });
        addBook(t, 10, S("2 Samuel"),       { S("2s"), S("2sa"), S("2 sam") });
        addBook(t, 11, S("1 Reyes"),        { S("1r"), S("1re"), S("1 rey") });
        addBook(t, 12, S("2 Reyes"),        { S("2r"), S("2re"), S("2 rey") });
        addBook(t, 13, S("1 Crónicas"),     { S("1cr"), S("1cro"), S("1 cr") });
        addBook(t, 14, S("2 Crónicas"),     { S("2cr"), S("2cro"), S("2 cr") });
        addBook(t, 15, S("Esdras"),         { S("esd") });
        addBook(t, 16, S("Nehemías"),       { S("neh") });
        addBook(t, 17, S("Ester"),          { S("est") });
        addBook(t, 18, S("Job"),            { S("job") });
        addBook(t, 19, S("Salmos"),         { S("sal"), S("salmo"), S("salmos"), S("ps") });
        addBook(t, 20, S("Proverbios"),     { S("pr"), S("prov"), S("pv") });
        addBook(t, 21, S("Eclesiastés"),    { S("ec"), S("ecle") });
        addBook(t, 22, S("Cantares"),       { S("cnt"), S("cant") });
        addBook(t, 23, S("Isaías"),         { S("is"), S("isa") });
        addBook(t, 24, S("Jeremías"),       { S("jer") });
        addBook(t, 25, S("Lamentaciones"),  { S("lam") });
        addBook(t, 26, S("Ezequiel"),       { S("ez"), S("eze") });
        addBook(t, 27, S("Daniel"),         { S("dn"), S("dan") });
        addBook(t, 28, S("Oseas"),          { S("os"), S("ose") });
        addBook(t, 29, S("Joel"),           { S("jl"), S("joel") });
        addBook(t, 30, S("Amós"),           { S("am"), S("amos") });
        addBook(t, 31, S("Obadías"),        { S("ob"), S("obd") });
        addBook(t, 32, S("Jonás"),          { S("jon") });
        addBook(t, 33, S("Miqueas"),        { S("miq") });
        addBook(t, 34, S("Nahúm"),          { S("nah") });
        addBook(t, 35, S("Habacuc"),        { S("hab") });
        addBook(t, 36, S("Sofonías"),       { S("sof") });
        addBook(t, 37, S("Hageo"),          { S("hag") });
        addBook(t, 38, S("Zacarías"),       { S("zac") });
        addBook(t, 39, S("Malaquías"),      { S("mal") });
        addBook(t, 40, S("Mateo"),          { S("mt"), S("mat") });
        addBook(t, 41, S("Marcos"),         { S("mr"), S("mar") });
        addBook(t, 42, S("Lucas"),          { S("lc"), S("luc") });
        addBook(t, 43, S("Juan"),           { S("jn"), S("ju"), S("jua") });
        addBook(t, 44, S("Hechos"),         { S("hch"), S("hech") });
        addBook(t, 45, S("Romanos"),        { S("ro"), S("rom"), S("rm") });
        addBook(t, 46, S("1 Corintios"),    { S("1co"), S("1 cor") });
        addBook(t, 47, S("2 Corintios"),    { S("2co"), S("2 cor") });
        addBook(t, 48, S("Gálatas"),        { S("gal") });
        addBook(t, 49, S("Efesios"),        { S("ef"), S("efe") });
        addBook(t, 50, S("Filipenses"),     { S("fil") });
        addBook(t, 51, S("Colosenses"),     { S("col") });
        addBook(t, 52, S("1 Tesalonicenses"), { S("1ts"), S("1 te") });
        addBook(t, 53, S("2 Tesalonicenses"), { S("2ts"), S("2 te") });
        addBook(t, 54, S("1 Timoteo"),      { S("1ti"), S("1 ti") });
        addBook(t, 55, S("2 Timoteo"),      { S("2ti"), S("2 ti") });
        addBook(t, 56, S("Tito"),           { S("tit") });
        addBook(t, 57, S("Filemón"),        { S("flm") });
        addBook(t, 58, S("Hebreos"),        { S("heb") });
        addBook(t, 59, S("Santiago"),       { S("stg"), S("sgt"), S("sant") });
        addBook(t, 60, S("1 Pedro"),        { S("1p"), S("1pe"), S("1 ped") });
        addBook(t, 61, S("2 Pedro"),        { S("2p"), S("2pe"), S("2 ped") });
        addBook(t, 62, S("1 Juan"),         { S("1jn"), S("1 juan") });
        addBook(t, 63, S("2 Juan"),         { S("2jn"), S("2 juan") });
        addBook(t, 64, S("3 Juan"),         { S("3jn"), S("3 juan") });
        addBook(t, 65, S("Judas"),          { S("jud") });
        addBook(t, 66, S("Apocalipsis"),    { S("ap"), S("apo"), S("apoc") });
        return t;
    }
};

#endif // LUMINA_BIBLEREF_H
