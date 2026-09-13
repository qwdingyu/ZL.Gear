using NUnit.Framework;
using ZL.Gear.Extensions.Data.Utilities;

namespace ZL.Gear.Extensions.Data.Tests
{
    [TestFixture]
    public class CsvFormatTests
    {
        [Test]
        public void ParseLine_支持逗号与引号字段()
        {
            var line = "1,\"12,5\",normal";
            var parts = CsvFormat.ParseLine(line);

            Assert.AreEqual(3, parts.Length);
            Assert.AreEqual("1", parts[0]);
            Assert.AreEqual("12,5", parts[1]);
            Assert.AreEqual("normal", parts[2]);
        }

        [Test]
        public void EscapeField_含逗号时加引号()
        {
            var escaped = CsvFormat.EscapeField("a,b");
            Assert.AreEqual("\"a,b\"", escaped);
        }

        [Test]
        public void EscapeField_含引号时双写转义()
        {
            var escaped = CsvFormat.EscapeField("say \"hi\"");
            Assert.AreEqual("\"say \"\"hi\"\"\"", escaped);
        }
    }
}
