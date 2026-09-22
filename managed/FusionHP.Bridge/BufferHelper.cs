// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  BufferHelper.cs : patrón de búfer uniforme de la ABI (fn(..., char* out,
//  int32_t cap, int32_t* needed)):
//    1) llamada con cap=0/out=null → needed (solo mide; el núcleo no asigna),
//    2) aloca needed bytes y llama de nuevo,
//    3) si el needed cambió entre medias (contenido dinámico), reintenta UNA
//       vez más con el último needed (bucle de 2 intentos tras la medición).
//  Cero asignaciones dentro de la DLL: todo búfer lo posee el lado gestionado.
// ============================================================================
using System;
using System.Text;

namespace fusion.bridge
{
    /// <summary>Firma uniforme de una llamada nativa con búfer de salida.</summary>
    public delegate int BufferCall(byte[] outBuf, int cap, out int needed);

    public static class BufferHelper
    {
        /// <summary>
        /// Ejecuta la llamada y devuelve EXACTAMENTE los bytes producidos
        /// (para APIs binarias como render_preview_png, needed = longitud).
        /// Lanza FusionException ante estados != OK/ERR_LIMIT.
        /// </summary>
        public static byte[] InvokeBytes(BufferCall call)
        {
            if (call == null) throw new ArgumentNullException("call");

            int needed;
            int st = call(null, 0, out needed);          // 1) medir
            if (st != FusionStatus.Ok && st != FusionStatus.ErrLimit)
                throw new FusionException(st, "La llamada nativa falló en la medición del búfer.");
            if (needed <= 0) return new byte[0];

            for (int attempt = 0; attempt < 2; attempt++) // 2) hasta 2 intentos reales
            {
                byte[] buf = new byte[needed];
                st = call(buf, buf.Length, out needed);   // 3) llenar
                if (st == FusionStatus.Ok)
                {
                    if (needed < 0 || needed > buf.Length) needed = buf.Length;
                    if (needed == buf.Length) return buf;
                    byte[] exact = new byte[needed];
                    Array.Copy(buf, exact, needed);
                    return exact;
                }
                if (st != FusionStatus.ErrLimit)
                    throw new FusionException(st, "La llamada nativa falló llenando el búfer.");
                if (needed <= 0)
                    throw new FusionException(FusionStatus.ErrLimit, "El núcleo reportó tamaño inválido (" + needed + ").");
                // needed cambió (contenido dinámico): realloc y un intento más.
            }
            throw new FusionException(FusionStatus.ErrLimit,
                "El tamaño del búfer no se estabilizó tras reintentos (needed=" + needed + ").");
        }

        /// <summary>Variante de texto: decodifica UTF-8 y elimina el NUL final.</summary>
        public static string InvokeText(BufferCall call)
        {
            byte[] raw = InvokeBytes(call);
            // needed = len+1 con NUL para APIs de texto; el binario nunca termina
            // en NUL legítimo, así que recortamos solo NULs finales explícitos.
            int len = raw.Length;
            while (len > 0 && raw[len - 1] == 0) len--;
            if (len == 0) return string.Empty;
            return new UTF8Encoding(false).GetString(raw, 0, len);
        }
    }
}
