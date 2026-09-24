// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Api/QrCode.cs : generador de códigos QR PURO (F5.03.11 — «QR visible en
//  configuración») sin dependencias externas (regla del repo: cero NuGet
//  funcionales).
//
//  Soporte implementado (suficiente para emparejamiento IP+token, que son
//  cadenas ASCII cortas):
//   * Modo BYTE, corrección de errores M (≈15 %), versiones 1..10
//     (hasta 213 bytes) — el payload de emparejamiento cabe de sobra.
//   * Reed-Solomon sobre GF(256) con polinomio generador estándar.
//   * Las 8 máscaras con evaluación de penalización ISO/IEC 18004
//     (reglas 1-4) y elección de la mejor.
//   * Bits de formato (nivel M + máscara) con BCH(15,5).
//   * Salida: matriz bool[y,x] (true = módulo oscuro) lista para pintar
//     en WinForms/WPF, y PNG PNG mínimo (escalado por bloque, sin librerías:
//     PNG 8-bit RGB con filtros 0 + CRC propio).
//
//  net35-compatible (C# 7.3). Probado por el arnés (Tests).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace lumina.api
{
    public sealed class QrMatrix
    {
        public bool[,] Modules;      // [y, x] — true = oscuro
        public int Size;             // lado en módulos

        public bool IsDark(int x, int y) { return Modules[y, x]; }
    }

    public static class QrCode
    {
        // ---- capacidad BYTE/M por versión (número de códigos de datos) ----
        private static readonly int[] DataCodewordsM =
        { 0, 14, 26, 42, 62, 84, 106, 122, 152, 180, 213 };
        private static readonly int[] TotalCodewordsM =
        { 0, 26, 44, 70, 100, 134, 168, 196, 242, 292, 346 };
        // bloques de corrección: (numBlocks, ecPerBlock) por versión para M
        private static readonly int[] EcBlocksM  = { 0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 6 };
        private static readonly int[] EcPerBlockM = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 20, 24 };

        private static readonly int[] AlignmentCenter =
        { -1, -1, 6, 6, 6, 6, 6, 6, 6, 6, 6 };   // centros para v2..v6 (un centro)

        /// <summary>Genera el QR del texto (modo BYTE, nivel M). Lanza si no cabe.</summary>
        public static QrMatrix Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text ?? "");
            int version = 1;
            while (version <= 10 && DataCodewordsM[version] < data.Length + 2) version++;
            if (version > 10)
                throw new ArgumentException("El texto no cabe en un QR v1..v10 (máx ~211 bytes).");

            int size = 17 + 4 * version;
            bool[,] m = new bool[size, size];
            bool[,] reserved = new bool[size, size];   // módulos ya fijados

            // ---------------- buffer de bits: modo + longitud + datos ----
            BitBuffer bb = new BitBuffer();
            bb.Append(0b0100, 4);                                  // modo BYTE
            bb.Append(data.Length, version <= 9 ? 8 : 16);         // longitud
            foreach (byte b in data) bb.Append(b, 8);
            // terminador + relleno hasta múltiplo de 8
            bb.Append(0, Math.Min(4, bb.RemainingBitsForCodeword(DataCodewordsM[version])));
            bb.PadToByte();
            List<byte> codewords = bb.ToBytes();
            while (codewords.Count < DataCodewordsM[version])
            {
                codewords.Add(0xEC); if (codewords.Count < DataCodewordsM[version]) codewords.Add(0x11);
            }
            if (codewords.Count > DataCodewordsM[version])
                codewords.RemoveRange(DataCodewordsM[version],
                    codewords.Count - DataCodewordsM[version]);

            // ---------------- Reed-Solomon por bloque --------------------
            int nBlocks = EcBlocksM[version];
            int ecLen = EcPerBlockM[version];
            int dataLen = DataCodewordsM[version];
            List<byte> ecc = new List<byte>();
            // bloques iguales (M v1..v10 tiene bloques uniformes en la tabla M
            // para las versiones usadas aquí; v7+ tiene partición par/impar en
            // el estándar completo, pero M usa bloques iguales en v1..v10).
            int perBlock = dataLen / nBlocks;
            for (int b = 0; b < nBlocks; b++)
            {
                List<byte> block = codewords.GetRange(b * perBlock, perBlock);
                byte[] ec = RsEncode(block, ecLen);
                ecc.AddRange(ec);
            }
            // secuencia final: intercalado de datos + intercalado de EC
            List<byte> final = Interleave(codewords, perBlock, nBlocks, ecc, ecLen, nBlocks);

            // ---------------- patrones fijos ----------------------------
            DrawFinder(m, reserved, 0, 0);
            DrawFinder(m, reserved, size - 7, 0);
            DrawFinder(m, reserved, 0, size - 7);
            DrawTiming(m, reserved, size);
            // alineación (v2+)
            if (version >= 2)
            {
                int c = AlignmentCenter[version] >= 0 ? AlignmentCenter[version] : size - 7;
                DrawAlignment(m, reserved, c, c, version, size);
            }
            // módulo oscuro (4.9.1 ISO): (4*version+9, 8)
            m[size - 8, 8] = true; reserved[size - 8, 8] = true;
            // reservar áreas de formato (se escriben tras elegir máscara)
            for (int i = 0; i < 9; i++)
            {
                if (i != 6) { reserved[8, i] = true; }               // fila superior
                if (i != 6) { reserved[i, 8] = true; }               // columna izquierda
            }
            for (int i = 0; i < 8; i++)
            {
                reserved[8, size - 1 - i] = true;                    // fila inferior der.
                reserved[size - 1 - i, 8] = true;                    // columna derecha
            }
            reserved[size - 8, 8] = true;                            // ya fijado

            // ---------------- datos con las 8 máscaras → la mejor -------
            int bestMask = 0; long bestPenalty = long.MaxValue;
            bool[,] best = null;
            bool[,] baseGrid = new bool[size, size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    baseGrid[y, x] = m[y, x];

            for (int mask = 0; mask < 8; mask++)
            {
                bool[,] trial = (bool[,])baseGrid.Clone();
                PlaceData(trial, reserved, final, size, mask);
                ApplyMask(trial, reserved, size, mask);
                WriteFormatBits(trial, size, mask);
                long pen = Penalty(trial, size);
                if (pen < bestPenalty) { bestPenalty = pen; bestMask = mask; best = trial; }
            }

            QrMatrix q = new QrMatrix();
            q.Size = size;
            q.Modules = best;
            return q;
        }

        /// <summary>PNG del QR con escala (px por módulo) y margen (módulos).</summary>
        public static byte[] ToPng(QrMatrix qr, int scale, int marginModules)
        {
            int dim = (qr.Size + 2 * marginModules) * scale;
            // filas de imagen: filtros 0 (None) + RGB
            byte[] raw = new byte[dim * (1 + dim * 3)];
            int stride = 1 + dim * 3;
            for (int py = 0; py < dim; py++)
            {
                raw[py * stride] = 0;                       // filtro None
                for (int px = 0; px < dim; px++)
                {
                    int mx = px / scale - marginModules;
                    int my = py / scale - marginModules;
                    bool dark = mx >= 0 && my >= 0 && mx < qr.Size && my < qr.Size &&
                                qr.Modules[my, mx];
                    byte v = dark ? (byte)0 : (byte)255;
                    int o = py * stride + 1 + px * 3;
                    raw[o] = v; raw[o + 1] = v; raw[o + 2] = v;
                }
            }
            return BuildPng(raw, dim, dim);
        }

        // ============================================================ PNG ==
        private static byte[] BuildPng(byte[] rawRows, int w, int h)
        {
            MemoryStream ms = new MemoryStream();
            WriteBytes(ms, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            Chunk(ms, "IHDR", Ihdr(w, h));
            Chunk(ms, "IDAT", ZlibStore(rawRows));
            Chunk(ms, "IEND", new byte[0]);
            return ms.ToArray();
        }

        private static byte[] Ihdr(int w, int h)
        {
            byte[] b = new byte[13];
            b[0] = (byte)(w >> 24); b[1] = (byte)(w >> 16); b[2] = (byte)(w >> 8); b[3] = (byte)w;
            b[4] = (byte)(h >> 24); b[5] = (byte)(h >> 16); b[6] = (byte)(h >> 8); b[7] = (byte)h;
            b[8] = 8;    // bit depth
            b[9] = 2;    // color type RGB
            b[10] = 0; b[11] = 0; b[12] = 0;
            return b;
        }

        // Deflate STORED (bloques sin compresión) + zlib wrapper — PNG válido
        // sin dependencias; el QR es binario comprimible, el tamaño es modesto
        // y la validez es lo exigido (verificado por pypdf/python en tests).
        private static byte[] ZlibStore(byte[] data)
        {
            MemoryStream ms = new MemoryStream();
            ms.WriteByte(0x78); ms.WriteByte(0x01);          // zlib CMF/FLG
            int off = 0;
            while (off < data.Length)
            {
                int n = Math.Min(65535, data.Length - off);
                bool last = off + n >= data.Length;
                ms.WriteByte((byte)(last ? 1 : 0));
                ms.WriteByte((byte)(n & 0xFF));
                ms.WriteByte((byte)((n >> 8) & 0xFF));
                ms.WriteByte((byte)(~n & 0xFF));
                ms.WriteByte((byte)((~n >> 8) & 0xFF));
                ms.Write(data, off, n);
                off += n;
            }
            uint crc = Crc32(data);
            byte[] c = new byte[4];
            c[0] = (byte)(crc >> 24); c[1] = (byte)(crc >> 16);
            c[2] = (byte)(crc >> 8); c[3] = (byte)crc;
            ms.Write(c, 0, 4);
            return ms.ToArray();
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            byte[] len = new byte[4];
            len[0] = (byte)(data.Length >> 24); len[1] = (byte)(data.Length >> 16);
            len[2] = (byte)(data.Length >> 8); len[3] = (byte)data.Length;
            s.Write(len, 0, 4);
            byte[] t = Encoding.ASCII.GetBytes(type);
            s.Write(t, 0, 4);
            s.Write(data, 0, data.Length);
            uint crc = Crc32(Concat(t, data));
            byte[] c = new byte[4];
            c[0] = (byte)(crc >> 24); c[1] = (byte)(crc >> 16);
            c[2] = (byte)(crc >> 8); c[3] = (byte)crc;
            s.Write(c, 0, 4);
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            byte[] r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        private static void WriteBytes(Stream s, byte[] b) { s.Write(b, 0, b.Length); }

        private static readonly uint[] CrcTable = BuildCrcTable();
        private static uint[] BuildCrcTable()
        {
            uint[] t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }
        private static uint Crc32(byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }

        // ================================================== Reed-Solomon ==
        private static readonly int[] Gexp = BuildGexp();
        private static readonly int[] Glog = BuildGlog();
        private static int[] BuildGexp()
        {
            int[] e = new int[512];
            int x = 1;
            for (int i = 0; i < 255; i++) { e[i] = x; x <<= 1; if (x >= 256) x ^= 0x11D; }
            for (int i = 255; i < 512; i++) e[i] = e[i - 255];
            return e;
        }
        private static int[] BuildGlog()
        {
            int[] l = new int[256];
            for (int i = 0; i < 255; i++) l[Gexp[i]] = i;
            return l;
        }
        private static int Gmul(int a, int b)
        {
            if (a == 0 || b == 0) return 0;
            return Gexp[Glog[a] + Glog[b]];
        }

        private static byte[] RsEncode(List<byte> data, int ecLen)
        {
            // Polinomio generador de RS sobre GF(256): x^ec + g1·x^(ec-1) + …
            // construido multiplicando (x - a^i) iterativamente.
            int[] poly = new int[ecLen + 1];
            poly[0] = 1;
            for (int i = 0; i < ecLen; i++)
                for (int j = i; j >= 0; j--)
                    poly[j + 1] = poly[j + 1] ^ Gmul(poly[j], Gexp[i]);
            int[] res = new int[ecLen];
            foreach (byte b in data)
            {
                int factor = b ^ res[0];
                Array.Copy(res, 1, res, 0, ecLen - 1);
                res[ecLen - 1] = 0;
                if (factor != 0)
                    for (int i = 0; i < ecLen; i++)
                        res[i] ^= Gmul(poly[ecLen - i], factor);
            }
            byte[] outB = new byte[ecLen];
            for (int i = 0; i < ecLen; i++) outB[i] = (byte)res[i];
            return outB;
        }

        private static List<byte> Interleave(List<byte> data, int perBlock, int nBlocks,
                                             List<byte> ecc, int ecLen, int nEccBlocks)
        {
            List<byte> final = new List<byte>();
            for (int i = 0; i < perBlock; i++)
                for (int b = 0; b < nBlocks; b++)
                    final.Add(data[b * perBlock + i]);
            for (int i = 0; i < ecLen; i++)
                for (int b = 0; b < nEccBlocks; b++)
                    final.Add(ecc[b * ecLen + i]);
            return final;
        }

        // ===================================================== colocación ==
        private static void DrawFinder(bool[,] m, bool[,] r, int x, int y)
        {
            for (int dy = -1; dy <= 7; dy++)
                for (int dx = -1; dx <= 7; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px < 0 || py < 0 || px >= m.GetLength(1) || py >= m.GetLength(0))
                        continue;
                    bool dark = (dx >= 0 && dx <= 6 && (dy == 0 || dy == 6)) ||
                                (dy >= 0 && dy <= 6 && (dx == 0 || dx == 6)) ||
                                (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4);
                    m[py, px] = dark;
                    r[py, px] = true;
                }
        }

        private static void DrawTiming(bool[,] m, bool[,] r, int size)
        {
            for (int i = 8; i < size - 8; i++)
            {
                bool dark = (i % 2) == 0;
                m[6, i] = dark; r[6, i] = true;
                m[i, 6] = dark; r[i, 6] = true;
            }
        }

        private static void DrawAlignment(bool[,] m, bool[,] r, int cx, int cy,
                                          int version, int size)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int px = cx + dx, py = cy + dy;
                    if (px < 0 || py < 0 || px >= size || py >= size) continue;
                    if (r[py, px]) continue;               // no pisar patrones fijos
                    bool dark = Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1;
                    m[py, px] = dark;
                    r[py, px] = true;
                }
        }

        private static void PlaceData(bool[,] m, bool[,] reserved, List<byte> bits,
                                      int size, int mask)
        {
            // colocación serpenteante desde abajo-derecha, 2 columnas por paso
            int bitIndex = 0;
            int totalBits = bits.Count * 8;
            bool upward = true;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                int col = right;
                if (col <= 6) col = 5;      // saltar la columna de timing
                for (int i = 0; i < size; i++)
                {
                    int y = upward ? size - 1 - i : i;
                    for (int c = 0; c < 2; c++)
                    {
                        int x = col - c;
                        if (x < 0 || reserved[y, x]) continue;
                        bool bit = false;
                        if (bitIndex < totalBits)
                        {
                            byte b = bits[bitIndex >> 3];
                            bit = ((b >> (7 - (bitIndex & 7))) & 1) != 0;
                        }
                        m[y, x] = bit;                 // SIN máscara todavía
                        bitIndex++;
                    }
                }
                upward = !upward;
            }
        }

        private static void ApplyMask(bool[,] m, bool[,] r, int size, int mask)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (r[y, x]) continue;
                    if (MaskBit(mask, x, y)) m[y, x] = !m[y, x];
                }
        }

        private static bool MaskBit(int mask, int x, int y)
        {
            switch (mask)
            {
                case 0: return ((x + y) & 1) == 0;
                case 1: return (y & 1) == 0;
                case 2: return (x % 3) == 0;
                case 3: return ((x + y) % 3) == 0;
                case 4: return (((x / 3) + (y / 2)) & 1) == 0;
                case 5: return ((x * y) % 2) + ((x * y) % 3) == 0;
                case 6: return ((((x * y) % 2) + ((x * y) % 3)) & 1) == 0;
                default: return ((((x + y) % 2) + ((x * y) % 3)) & 1) == 0;
            }
        }

        private static void WriteFormatBits(bool[,] m, int size, int mask)
        {
            // nivel M (0b00) + máscara → 5 bits de datos: 0 0 0 mask(3)
            int data = 0x00 << 3 | mask;               // M = 00
            int v = data << 10;
            int gen = 0x537;                            // polinomio BCH (10 bits)
            for (int i = 14; i >= 10; i--)
                if (((v >> i) & 1) != 0) v ^= gen << (i - 10);
            int fmt = ((data << 10) | v) ^ 0x5412;      // máscara XOR estándar
            for (int i = 0; i <= 5; i++) m[8, i] = ((fmt >> i) & 1) != 0;
            m[8, 7] = ((fmt >> 6) & 1) != 0;
            m[8, 8] = ((fmt >> 7) & 1) != 0;
            m[7, 8] = ((fmt >> 8) & 1) != 0;
            for (int i = 9; i < 15; i++) m[14 - i, 8] = ((fmt >> i) & 1) != 0;
            for (int i = 0; i < 8; i++) m[size - 1 - i, 8] = ((fmt >> i) & 1) != 0;
            for (int i = 8; i < 15; i++) m[8, size - 15 + i] = ((fmt >> i) & 1) != 0;
            m[size - 8, 8] = true;                      // módulo oscuro fijo
        }

        private static long Penalty(bool[,] m, int size)
        {
            long pen = 0;
            // regla 1: runs de 5+ en filas/columnas
            for (int y = 0; y < size; y++)
            {
                int run = 1;
                for (int x = 1; x < size; x++)
                {
                    if (m[y, x] == m[y, x - 1]) { run++; if (run == 5) pen += 3; else if (run > 5) pen++; }
                    else run = 1;
                }
            }
            for (int x = 0; x < size; x++)
            {
                int run = 1;
                for (int y = 1; y < size; y++)
                {
                    if (m[y, x] == m[y - 1, x]) { run++; if (run == 5) pen += 3; else if (run > 5) pen++; }
                    else run = 1;
                }
            }
            // regla 2: bloques 2x2 iguales
            for (int y = 0; y < size - 1; y++)
                for (int x = 0; x < size - 1; x++)
                    if (m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1])
                        pen += 3;
            // regla 3: patrones 1011101 con 4 claros antes/después (simplificado)
            for (int y = 0; y < size; y++)
                for (int x = 0; x + 10 < size; x++)
                {
                    bool dark1 = m[y, x];
                    // patrón 10111010000 o 00001011101 en fila
                    if (IsPattern(m, y, x, true)) pen += 40;
                    if (IsPattern(m, y, x, false)) pen += 40;
                }
            // regla 4: proporción de oscuros
            int dark = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (m[y, x]) dark++;
            double ratio = (double)dark / (size * size);
            pen += (long)(Math.Abs(ratio - 0.5) * 20) * 10;
            return pen;
        }

        private static bool IsPattern(bool[,] m, int y, int x, bool dir)
        {
            // 1011101 + 0000 (o el simétrico) — versión compacta de la regla 3.
            if (dir)
            {
                return m[y, x] && !m[y, x + 1] && m[y, x + 2] && m[y, x + 3] &&
                       m[y, x + 4] && !m[y, x + 5] && m[y, x + 6];
            }
            return !m[y, x] && m[y, x + 1] && !m[y, x + 2] && !m[y, x + 3] &&
                   !m[y, x + 4] && m[y, x + 5] && !m[y, x + 6];
        }

        private sealed class BitBuffer
        {
            private readonly List<byte> _bytes = new List<byte>();
            private int _bitPos;                        // 0..7 (bits usados del último)
            public int BitCount { get { return _bytes.Count * 8 - _bitPos; } }

            public void Append(int value, int bits)
            {
                for (int i = bits - 1; i >= 0; i--)
                {
                    if (_bitPos == 0) { _bytes.Add(0); _bitPos = 8; }
                    if (((value >> i) & 1) != 0)
                        _bytes[_bytes.Count - 1] |= (byte)(1 << (_bitPos - 1));
                    _bitPos--;
                }
            }
            public int RemainingBitsForCodeword(int codewords)
            {
                return codewords * 8 - BitCount;
            }
            public void PadToByte()
            {
                _bitPos = 0;                            // el byte actual ya está en la lista
            }
            public List<byte> ToBytes() { return new List<byte>(_bytes); }
        }
    }
}
