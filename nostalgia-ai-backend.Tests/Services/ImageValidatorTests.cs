using System.Text;
using Infrastructure.Services;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class ImageValidatorTests
    {
        private static MemoryStream StreamOf(params byte[][] parts)
        {
            var bytes = parts.SelectMany(p => p).ToArray();
            // Pad so short signatures still fill the 12-byte header read.
            var padded = bytes.Concat(new byte[Math.Max(0, 32 - bytes.Length)]).ToArray();
            return new MemoryStream(padded);
        }

        [Fact]
        public void Accepts_a_jpeg_header()
        {
            using var stream = StreamOf(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

            Assert.True(ImageValidator.TryResolve(stream, out var contentType, out var extension));
            Assert.Equal("image/jpeg", contentType);
            Assert.Equal(".jpg", extension);
        }

        [Fact]
        public void Accepts_a_png_header()
        {
            using var stream = StreamOf(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            Assert.True(ImageValidator.TryResolve(stream, out var contentType, out var extension));
            Assert.Equal("image/png", contentType);
            Assert.Equal(".png", extension);
        }

        [Fact]
        public void Accepts_a_webp_header()
        {
            using var stream = StreamOf(
                Encoding.ASCII.GetBytes("RIFF"),
                new byte[] { 0x00, 0x00, 0x00, 0x00 },
                Encoding.ASCII.GetBytes("WEBP"));

            Assert.True(ImageValidator.TryResolve(stream, out var contentType, out var extension));
            Assert.Equal("image/webp", contentType);
            Assert.Equal(".webp", extension);
        }

        [Fact]
        public void Rejects_a_text_file_that_was_renamed_to_look_like_an_image()
        {
            using var stream = StreamOf(Encoding.ASCII.GetBytes("This is definitely not a picture."));

            Assert.False(ImageValidator.TryResolve(stream, out _, out _));
        }

        [Fact]
        public void Rejects_an_executable()
        {
            using var stream = StreamOf(Encoding.ASCII.GetBytes("MZ"), new byte[] { 0x90, 0x00 });

            Assert.False(ImageValidator.TryResolve(stream, out _, out _));
        }

        [Fact]
        public void Rejects_a_riff_container_that_is_not_webp()
        {
            using var stream = StreamOf(
                Encoding.ASCII.GetBytes("RIFF"),
                new byte[] { 0x00, 0x00, 0x00, 0x00 },
                Encoding.ASCII.GetBytes("WAVE"));

            Assert.False(ImageValidator.TryResolve(stream, out _, out _));
        }

        [Fact]
        public void Rewinds_the_stream_so_it_can_still_be_uploaded()
        {
            using var stream = StreamOf(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

            Assert.True(ImageValidator.TryResolve(stream, out _, out _));
            Assert.Equal(0, stream.Position);
        }

        [Fact]
        public void Rejects_an_empty_stream()
        {
            using var stream = new MemoryStream();

            Assert.False(ImageValidator.TryResolve(stream, out _, out _));
        }
    }
}
