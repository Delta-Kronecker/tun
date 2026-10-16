using System;
using System.Linq;
using System.Net;
using Se7enPro.Services;
using Xunit;

namespace Se7enTun.Tests;

public sealed class DnsWireFormatTests
{
    [Fact]
    public void BuildQuery_round_trips_through_ParseQuestion()
    {
        var query = SocksDnsForwarder.BuildQuery("example.com");

        Assert.Equal(29, query.Length);
        Assert.Equal(0x01, query[2]);
        Assert.Equal(1, query[5]);

        var parsed = SocksDnsForwarder.ParseQuestion(query);
        Assert.NotNull(parsed);
        Assert.Equal("example.com", parsed.Value.Name);
        Assert.Equal((ushort)1, parsed.Value.Type);
        Assert.Equal(29, parsed.Value.QuestionLength);
    }

    [Fact]
    public void BuildQuery_uses_length_prefixed_labels()
    {
        var query = SocksDnsForwarder.BuildQuery("a.bc");

        Assert.Equal(22, query.Length);
        Assert.Equal(1, query[12]);
        Assert.Equal((byte)'a', query[13]);
        Assert.Equal(2, query[14]);
        Assert.Equal((byte)'b', query[15]);
        Assert.Equal((byte)'c', query[16]);
        Assert.Equal(0, query[17]);
        Assert.Equal(0, query[18]);
        Assert.Equal(1, query[19]);
        Assert.Equal(0, query[20]);
        Assert.Equal(1, query[21]);
    }

    [Fact]
    public void BuildQuery_rejects_an_empty_label()
    {
        Assert.Throws<ArgumentException>(() => SocksDnsForwarder.BuildQuery("a..b"));
    }

    [Fact]
    public void ParseQuestion_rejects_a_truncated_buffer()
    {
        Assert.Null(SocksDnsForwarder.ParseQuestion(new byte[10]));
    }

    [Fact]
    public void ExtractAnswerARecords_reads_the_answer_section()
    {
        var query = SocksDnsForwarder.BuildQuery("example.com");
        var response = new byte[query.Length + 16];
        Buffer.BlockCopy(query, 0, response, 0, query.Length);

        response[7] = 1;
        response[29] = 0xC0;
        response[30] = 0x0C;
        response[31] = 0x00;
        response[32] = 0x01;
        response[33] = 0x00;
        response[34] = 0x01;
        response[39] = 0x00;
        response[40] = 0x04;
        response[41] = 1;
        response[42] = 2;
        response[43] = 3;
        response[44] = 4;

        var records = SocksDnsForwarder.ExtractAnswerARecords(response);

        Assert.Single(records);
        Assert.Equal(IPAddress.Parse("1.2.3.4"), records[0]);
    }

    [Fact]
    public void ExtractAnswerARecords_returns_nothing_without_answers()
    {
        Assert.Empty(SocksDnsForwarder.ExtractAnswerARecords(new byte[12]));
        Assert.Empty(SocksDnsForwarder.ExtractAnswerARecords(Array.Empty<byte>()));
    }

    [Fact]
    public void BuildEmptyResponse_echoes_the_question_and_clears_counts()
    {
        var query = SocksDnsForwarder.BuildQuery("example.com");
        var response = SocksDnsForwarder.BuildEmptyResponse(query, query.Length);

        Assert.Equal(query.Length, response.Length);
        Assert.True((response[2] & 0x80) != 0);
        Assert.Equal(0, response[6]);
        Assert.Equal(0, response[7]);
        Assert.Equal(query.Skip(12).ToArray(), response.Skip(12).ToArray());
    }

    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("www.example.com", "example.com")]
    [InlineData("EXAMPLE.com", "example.com")]
    [InlineData("notexample.com", null)]
    [InlineData("example.com.evil.net", null)]
    [InlineData("other.test", null)]
    public void MatchDomain_matches_exact_and_subdomains(string name, string? expected)
    {
        var domains = new[] { "example.com" };
        Assert.Equal(expected, SocksDnsForwarder.MatchDomain(name, domains));
    }
}
