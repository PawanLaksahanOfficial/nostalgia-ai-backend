using Infrastructure.Services;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class PhotoCreditBuilderTests
    {
        [Fact]
        public void Names_each_photographer_once_and_the_site()
        {
            var credit = PhotoCreditBuilder.Build(new[] { "Ana Silva", " Ravi Perera ", "ana silva" }, "Pixabay", 500);

            Assert.Equal("Ana Silva, Ravi Perera on Pixabay", credit);
        }

        [Fact]
        public void Gives_no_credit_when_no_photographer_is_named()
        {
            Assert.Null(PhotoCreditBuilder.Build(new[] { "", "  " }, "Pixabay", 500));
        }

        [Fact]
        public void Shortens_the_names_but_always_keeps_the_site()
        {
            var credit = PhotoCreditBuilder.Build(new[] { "Ana Silva", "Ravi Perera" }, "Pixabay", 20);

            Assert.Equal("Ana Silva on Pixabay", credit);
        }
    }
}
