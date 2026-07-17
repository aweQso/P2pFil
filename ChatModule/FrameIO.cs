using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace P2PFil.ChatModule
{
    // NetworkStream üzerinde uzunluk-öncekli (length-prefixed) çerçeveleri
    // tamamen ASENKRON ve CancellationToken'a duyarlı şekilde okuyup yazan
    // yardımcı sınıf.
    //
    // GÜVENLİK NOTU: BinaryReader/BinaryWriter'ın senkron metotları (ReadInt32,
    // ReadBytes, ReadByte, WriteByte) NetworkStream üzerinde CancellationToken'ı
    // HİÇBİR ŞEKİLDE gözetmez. Bu yüzden önceden tanımlanan CancellationTokenSource
    // zaman aşımları, karşı taraf veri göndermeyi durdurduğunda devreye girmiyor
    // ve bağlantı süresiz bloke kalabiliyordu (DoS / kaynak tükenmesi açığı).
    // Bu sınıf tüm okuma/yazmaları gerçek async I/O (ReadAsync/WriteAsync) ile
    // yapar, böylece verilen ct HER ZAMAN gözetilir.
    internal static class FrameIO
    {
        public static async Task<int> ReadInt32Async(NetworkStream stream, CancellationToken ct)
        {
            byte[] buf = await ReadExactAsync(stream, 4, ct);
            return BitConverter.ToInt32(buf, 0);
        }

        public static async Task WriteInt32Async(NetworkStream stream, int value, CancellationToken ct)
        {
            byte[] buf = BitConverter.GetBytes(value);
            await stream.WriteAsync(buf, 0, buf.Length, ct);
        }

        public static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
        {
            if (count == 0) return Array.Empty<byte>();
            byte[] buf = new byte[count];
            int total = 0;
            while (total < count)
            {
                int read = await stream.ReadAsync(buf, total, count - total, ct);
                if (read == 0) throw new IOException("Bağlantı beklenenden erken kapandı.");
                total += read;
            }
            return buf;
        }

        public static async Task WriteBytesAsync(NetworkStream stream, byte[] data, CancellationToken ct)
        {
            if (data.Length > 0)
                await stream.WriteAsync(data, 0, data.Length, ct);
        }

        // Tek byte'lık el sıkışma bayrakları için (ör. oturum yeniden kullanım isteği).
        // Akış kapanmışsa -1 döner (eski senkron stream.ReadByte() ile aynı sözleşme).
        public static async Task<int> ReadByteOrEofAsync(NetworkStream stream, CancellationToken ct)
        {
            byte[] buf = new byte[1];
            int read = await stream.ReadAsync(buf, 0, 1, ct);
            return read == 0 ? -1 : buf[0];
        }

        public static Task WriteByteAsync(NetworkStream stream, byte value, CancellationToken ct)
        {
            return stream.WriteAsync(new[] { value }, 0, 1, ct);
        }

        // "Uzunluk + veri" çerçevesi yazan/okuyan kısayollar (simetrik protokol adımları için).
        public static async Task WriteFrameAsync(NetworkStream stream, byte[] data, CancellationToken ct)
        {
            await WriteInt32Async(stream, data.Length, ct);
            await WriteBytesAsync(stream, data, ct);
            await stream.FlushAsync(ct);
        }

        public static async Task<byte[]> ReadFrameAsync(NetworkStream stream, int maxSize, CancellationToken ct, string errorMessage = "Geçersiz çerçeve boyutu tespit edildi.")
        {
            int len = await ReadInt32Async(stream, ct);
            if (len < 0 || len > maxSize)
                throw new InvalidDataException($"{errorMessage} ({len} bytes)");
            return await ReadExactAsync(stream, len, ct);
        }

        // YENİ: Zero-allocation büyük dosya transferi için ArrayPool tabanlı çerçeve okuma.
        // Var olan ReadFrameAsync ile TAMAMEN AYNI tel-protokolünü (uzunluk + veri) kullanır;
        // tek fark, dönen buffer'ın System.Buffers.ArrayPool<byte>.Shared'dan kiralanmış
        // olması ve GERÇEK VERİ BOYUTUNUN 'length' out parametresiyle ayrıca dönmesidir
        // (kiralanan buffer genelde istenenden BÜYÜK olur, bu yüzden buf.Length güvenilir değildir).
        //
        // ÇAĞIRAN SORUMLULUĞU: dönen buffer, işi bitince MUTLAKA
        // ArrayPool<byte>.Shared.Return(buffer) ile iade edilmelidir (try/finally içinde).
        // Bu metot şifreleme/el sıkışma protokolüne dokunmaz; sadece byte taşıma
        // katmanında ekstra tahsis yapmaktan kaçınır.
        public static async Task<(byte[] Buffer, int Length)> ReadFramePooledAsync(
            NetworkStream stream, int maxSize, CancellationToken ct,
            string errorMessage = "Geçersiz çerçeve boyutu tespit edildi.")
        {
            int len = await ReadInt32Async(stream, ct);
            if (len < 0 || len > maxSize)
                throw new InvalidDataException($"{errorMessage} ({len} bytes)");

            if (len == 0)
                return (Array.Empty<byte>(), 0);

            byte[] buf = System.Buffers.ArrayPool<byte>.Shared.Rent(len);
            int total = 0;
            try
            {
                while (total < len)
                {
                    int read = await stream.ReadAsync(buf, total, len - total, ct);
                    if (read == 0) throw new IOException("Bağlantı beklenenden erken kapandı.");
                    total += read;
                }
            }
            catch
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buf);
                throw;
            }

            return (buf, total);
        }

        // YENİ: ArrayPool'dan kiralanmış bir buffer'ı (dizinin tamamı değil,
        // sadece 'length' kadarı geçerli veri) çerçeve olarak yazar. Var olan
        // WriteFrameAsync ile aynı tel-protokolü, ekstra kopyalama olmadan.
        public static async Task WriteFramePooledAsync(NetworkStream stream, byte[] rentedBuffer, int length, CancellationToken ct)
        {
            await WriteInt32Async(stream, length, ct);
            if (length > 0)
                await stream.WriteAsync(rentedBuffer, 0, length, ct);
            await stream.FlushAsync(ct);
        }
    }
}
