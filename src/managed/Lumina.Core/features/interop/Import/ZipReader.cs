// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ZipReader.cs (v6.0.0 «HORIZONTE») : lector ZIP/OPC MÍNIMO para IMPORTAR
//  PPTX (roadmap: «leer PresentationML con el mismo motor OPC/ZipWriter
//  propio»). Simétrico de ZipWriter: camina el central directory (no el
//  flujo local), soporta método 0 (stored) y 8 (deflate), nombres UTF-8.
//
//  Decisión de diseño: System.IO.Compression.ZipArchive requiere
//  System.IO.Compression.FileSystem (net45+) y resuelve distinto entre CLR2 y
//  CLR4 — igual que System.IO.Packaging antes (lección v5.1.0 del ZipWriter).
//  Leer el formato PKWARE APPNOTE.TXT nosotros mismos mantiene la capa net35
//  con CERO dependencias del GAC: misma base de código para 3.5/4.8/8.
//  - Solo LECTURA de entradas existentes; sin escritura ni cifrado (AES no
//    soportado → excepción clara; ZIPCrypto legacy no aparece en PPTX).
//  - Compresión máxima tolerada: 64 MB por entrada (defensa de RAM).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace lumina.core
{
    /// <summary>Entrada leída (nombre normalizado con '/' + bytes inflados).</summary>
    public sealed class ZipEntryData
    {
        public string Name;
        public byte[] Data;
    }

    public static class ZipReader
    {
        private const int MaxEntryBytes = 64 * 1024 * 1024;   // 64 MB por entrada
        private const long MaxZipBytes  = 512L * 1024 * 1024; // 512 MB por archivo

        /// <summary>
        /// Lee TODAS las entradas del stream (posición actual → EOF). Lanza
        /// excepciones descriptivas si el ZIP es inválido o usa cifrado.
        /// El stream se lee completo a bytes (los PPTX reales caben holgado;
        /// el límite de 512 MB evita descomprimir bombas).
        /// </summary>
        public static List<ZipEntryData> ReadAll(Stream input)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (!input.CanRead) throw new ArgumentException("El stream no es legible.");

            byte[] zip = ReadFully(input);
            if (zip.Length < 22) throw new InvalidDataException("Archivo ZIP truncado (menor que EOCD).");

            int eocd = FindEocd(zip);
            if (eocd < 0) throw new InvalidDataException("Firma EOCD (0x06054b50) no encontrada.");
            int count = ZipLow.ReadU16(zip, eocd + 10);
            int cdOff = (int)ZipLow.ReadU32(zip, eocd + 16);

            List<ZipEntryData> entries = new List<ZipEntryData>(count);
            int p = cdOff;
            for (int i = 0; i < count; i++)
            {
                if (p + 46 > zip.Length || ZipLow.ReadU32(zip, p) != 0x02014b50)
                    throw new InvalidDataException("Central directory corrupto (entrada " + i + ").");
                ZipEntryData e = new ZipEntryData();
                int method  = ZipLow.ReadU16(zip, p + 10);
                int flags   = ZipLow.ReadU16(zip, p + 8);
                int crc     = (int)ZipLow.ReadU32(zip, p + 16);
                long comp   = ZipLow.ReadU32(zip, p + 20);
                long uncomp = ZipLow.ReadU32(zip, p + 24);
                int nameLen = ZipLow.ReadU16(zip, p + 28);
                int extraLen= ZipLow.ReadU16(zip, p + 30);
                int commLen = ZipLow.ReadU16(zip, p + 32);
                int localOff= (int)ZipLow.ReadU32(zip, p + 42);
                bool utf8 = (flags & 0x0800) != 0;
                e.Name = DecodeName(zip, p + 46, nameLen, utf8);

                if ((flags & 0x0001) != 0)
                    throw new InvalidDataException("Entrada cifrada no soportada: " + e.Name);
                if (uncomp > MaxEntryBytes)
                    throw new InvalidDataException("Entrada demasiado grande: " + e.Name +
                        " (" + uncomp + " bytes > 64 MB).");

                e.Data = ReadLocal(zip, localOff, method, comp, uncomp, crc, e.Name);
                entries.Add(e);
                p += 46 + nameLen + extraLen + commLen;
            }
            return entries;
        }

        /* ------------------------------------------------------- local ---- */

        private static byte[] ReadLocal(byte[] zip, int off, int method,
                                        long comp, long uncomp, int crc, string name)
        {
            if (off + 30 > zip.Length || ZipLow.ReadU32(zip, off) != 0x04034b50)
                throw new InvalidDataException("Local header corrupto: " + name);
            int nameLen = ZipLow.ReadU16(zip, off + 26);
            int extraLen= ZipLow.ReadU16(zip, off + 28);
            int dataOff = off + 30 + nameLen + extraLen;

            // 0xFFFFFFFF (ZIP64) → fuera del alcance deliberadamente soportado
            if (comp >= 0xFFFFFFFFL || uncomp >= 0xFFFFFFFFL)
                throw new InvalidDataException("Entrada ZIP64 no soportada: " + name);

            int clen = (int)comp, ulen = (int)uncomp;
            if (dataOff + clen > zip.Length)
                throw new InvalidDataException("Datos truncados: " + name);

            byte[] outBuf;
            if (method == 0)
            {
                // stored: bytes tal cual
                if (clen != ulen)
                    throw new InvalidDataException("Entrada stored inconsistente: " + name);
                outBuf = new byte[clen];
                Buffer.BlockCopy(zip, dataOff, outBuf, 0, clen);
            }
            else if (method == 8)
            {
                outBuf = new byte[ulen];
                using (MemoryStream src = new MemoryStream(zip, dataOff, clen, false))
                using (DeflateStream ds = new DeflateStream(src, CompressionMode.Decompress))
                {
                    int total = 0;
                    while (total < ulen)
                    {
                        int n = ds.Read(outBuf, total, ulen - total);
                        if (n <= 0) break;
                        total += n;
                    }
                    if (total != ulen)
                        throw new InvalidDataException("Deflate truncado: " + name);
                }
            }
            else
            {
                throw new InvalidDataException("Método de compresión " + method +
                    " no soportado: " + name);
            }

            // CRC32 (tabla IEEE — la MISMA de ZipWriter): integridad.
            if ((int)ZipLow.Crc32(outBuf, 0, outBuf.Length) != crc)
                throw new InvalidDataException("CRC32 no coincide: " + name);
            return outBuf;
        }

        private static int FindEocd(byte[] zip)
        {
            // EOCD puede llevar comentario de hasta 64 KB: buscar desde el final.
            for (int i = zip.Length - 22; i >= Math.Max(0, zip.Length - 22 - 65536); i--)
            {
                if (ZipLow.ReadU32(zip, i) == 0x06054b50) return i;
            }
            return -1;
        }

        private static byte[] ReadFully(Stream input)
        {
            MemoryStream ms = new MemoryStream();
            byte[] buf = new byte[8192];
            for (;;)
            {
                int n = input.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                ms.Write(buf, 0, n);
                if (ms.Length > MaxZipBytes)
                    throw new InvalidDataException("ZIP demasiado grande (límite 512 MB).");
            }
            return ms.ToArray();
        }

        private static string DecodeName(byte[] zip, int off, int len, bool utf8)
        {
            if (len == 0) return string.Empty;
            return utf8
                ? Encoding.UTF8.GetString(zip, off, len)
                : Codepage1252().GetString(zip, off, len);   // legacy CP437/ANSI tolerado
        }

        private static Encoding Codepage1252()
        {
#if LUMINA_NET35
            // net35: GetEncoding siempre disponible en la BCL de Framework.
            return Encoding.GetEncoding(1252);
#else
            try
            {
                // net8: CodePagesEncodingProvider solo si se registró (no requerido aquí).
                return Encoding.GetEncoding(1252);
            }
            catch (Exception)
            {
                return Encoding.UTF8;   // UTF-8 estricto como fallback
            }
#endif
        }

    }
}
