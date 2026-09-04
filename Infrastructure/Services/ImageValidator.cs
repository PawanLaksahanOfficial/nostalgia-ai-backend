namespace Infrastructure.Services
{
    public static class ImageValidator
    {
        private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF };
        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly byte[] Riff = { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"
        private static readonly byte[] Webp = { 0x57, 0x45, 0x42, 0x50 }; // "WEBP"

        public static bool TryResolve(Stream stream, out string contentType, out string extension)
        {
            contentType = string.Empty;
            extension = string.Empty;

            if (stream == null || !stream.CanRead)
            {
                return false;
            }
            var header = new byte[12];
            var read = ReadHeader(stream, header);
            if (read >= Jpeg.Length && StartsWith(header, Jpeg))
            {
                contentType = "image/jpeg";
                extension = ".jpg";
                return true;
            }
            if (read >= Png.Length && StartsWith(header, Png))
            {
                contentType = "image/png";
                extension = ".png";
                return true;
            }
            // WebP is a RIFF container: "RIFF" ---- "WEBP".
            if (read >= 12 && StartsWith(header, Riff) && MatchesAt(header, Webp, 8))
            {
                contentType = "image/webp";
                extension = ".webp";
                return true;
            }
            return false;
        }

        private static int ReadHeader(Stream stream, byte[] buffer)
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }
            var read = 0;
            while (read < buffer.Length)
            {
                var chunk = stream.Read(buffer, read, buffer.Length - read);
                if (chunk == 0)
                {
                    break;
                }

                read += chunk;
            }
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }
            return read;
        }

        private static bool StartsWith(byte[] buffer, byte[] signature) =>
            MatchesAt(buffer, signature, 0);

        private static bool MatchesAt(byte[] buffer, byte[] signature, int offset)
        {
            if (buffer.Length < offset + signature.Length)
            {
                return false;
            }
            for (var i = 0; i < signature.Length; i++)
            {
                if (buffer[offset + i] != signature[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
