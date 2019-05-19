﻿using System.Net.Http;
using System.Threading.Tasks;
using Amazon.Util;
using AWS.SignatureVersion4.Private;
using AWS.SignatureVersion4.TestSuite;
using Shouldly;
using Xunit;

namespace AWS.SignatureVersion4.Unit.Private
{
    public class CanonicalRequestShould : IClassFixture<TestSuiteContext>
    {
        private readonly TestSuiteContext context;

        public CanonicalRequestShould(TestSuiteContext context)
        {
            this.context = context;
        }

        [Theory]
        [InlineData("get-header-key-duplicate")]
        [InlineData("get-header-value-multiline")]
        [InlineData("get-header-value-order")]
        [InlineData("get-header-value-trim")]
        [InlineData("get-unreserved")]
        [InlineData("get-utf8")]
        [InlineData("get-vanilla")]
        [InlineData("get-vanilla-empty-query-key")]
        [InlineData("get-vanilla-query")]
        [InlineData("get-vanilla-query-order-key")]
        [InlineData("get-vanilla-query-order-key-case")]
        [InlineData("get-vanilla-query-order-value")]
        [InlineData("get-vanilla-query-unreserved")]
        [InlineData("get-vanilla-utf8-query")]
        [InlineData("normalize-path", "get-relative")]
        [InlineData("normalize-path", "get-relative-relative")]
        [InlineData("normalize-path", "get-slash")]
        [InlineData("normalize-path", "get-slash-dot-slash")]
        [InlineData("normalize-path", "get-slashes")]
        [InlineData("normalize-path", "get-slash-pointless-dot")]
        [InlineData("normalize-path", "get-space")]
        [InlineData("post-header-key-case")]
        [InlineData("post-header-key-sort")]
        [InlineData("post-header-value-case")]
        [InlineData("post-sts-token", "post-sts-header-after")]
        [InlineData("post-sts-token", "post-sts-header-before")]
        [InlineData("post-vanilla")]
        [InlineData("post-vanilla-empty-query-value")]
        [InlineData("post-vanilla-query")]
        [InlineData("post-x-www-form-urlencoded", Skip = SkipReasons.UnsupportedHeaders)]
        [InlineData("post-x-www-form-urlencoded-parameters", Skip = SkipReasons.UnsupportedHeaders)]
        public async Task PassTestSuite(params string[] scenarioName)
        {
            // Arrange
            var scenario = context.LoadScenario(scenarioName);

            // Add header 'X-Amz-Date' since the algorithm at this point expects it on the request
            scenario.Request.AddHeader(HeaderKeys.XAmzDateHeader, context.UtcNow.ToIso8601BasicDateTime());

            // Act
            var (canonicalRequest, signedHeaders) = await CanonicalRequest.BuildAsync(scenario.Request);

            // Assert
            canonicalRequest.ShouldBe(scenario.ExpectedCanonicalRequest);
            signedHeaders.ShouldBe(scenario.ExpectedSignedHeaders);
        }

        [Theory]
        [InlineData("A", "a")]
        [InlineData("AA", "aa")]
        [InlineData("A-A", "a-a")]
        public void LowerCaseHeaderNames(string headerName, string expected)
        {
            // Arrange
            var headers = new HttpRequestMessage().Headers;
            headers.Add(headerName, "some header value");

            // Act
            var actual = CanonicalRequest.SortHeaders(headers);

            // Assert
            actual.Keys.ShouldBe(new[] { expected });
        }

        [Theory]
        [InlineData(new[] { "Aa", "Bb", "Cc" }, new[] { "aa", "bb", "cc" })]
        [InlineData(new[] { "Aa", "Cc", "Bb" }, new[] { "aa", "bb", "cc" })]
        [InlineData(new[] { "Bb", "Aa", "Cc" }, new[] { "aa", "bb", "cc" })]
        [InlineData(new[] { "Bb", "Cc", "Aa" }, new[] { "aa", "bb", "cc" })]
        [InlineData(new[] { "Cc", "Aa", "Bb" }, new[] { "aa", "bb", "cc" })]
        [InlineData(new[] { "Cc", "Bb", "Aa" }, new[] { "aa", "bb", "cc" })]
        public void SortHeaderNames(string[] headerNames, string[] expected)
        {
            // Arrange
            var headers = new HttpRequestMessage().Headers;

            foreach (var headerName in headerNames)
            {
                headers.Add(headerName, "some header value");
            }

            // Act
            var actual = CanonicalRequest.SortHeaders(headers);

            // Assert
            actual.Keys.ShouldBe(expected);
        }

        [Theory]
        [InlineData(" A", "A")]
        [InlineData("A ", "A")]
        [InlineData(" A ", "A")]
        [InlineData(" AA", "AA")]
        [InlineData("AA ", "AA")]
        [InlineData(" AA ", "AA")]
        [InlineData(" A A", "A A")]
        [InlineData("A A ", "A A")]
        [InlineData(" A A ", "A A")]
        public void TrimHeaderValue(string headerValue, string expected)
        {
            // Arrange
            var headers = new HttpRequestMessage().Headers;
            headers.Add("some-header-name", headerValue);

            // Act
            var actual = CanonicalRequest.SortHeaders(headers);

            // Assert
            actual["some-header-name"].ShouldBe(new[] { expected });
        }

        [Theory]
        [InlineData("A A", "A A")]
        [InlineData("A  A", "A A")]
        [InlineData("A   A", "A A")]
        [InlineData("A A A", "A A A")]
        [InlineData("A  A  A", "A A A")]
        [InlineData("A   A   A", "A A A")]
        public void RemoveSequentialSpacesInHeaderValue(string headerValue, string expected)
        {
            // Arrange
            var headers = new HttpRequestMessage().Headers;
            headers.Add("some-header-name", headerValue);

            // Act
            var actual = CanonicalRequest.SortHeaders(headers);

            // Assert
            actual["some-header-name"].ShouldBe(new[] { expected });
        }

        [Theory]
        [InlineData("?a=1&b=2&c=3", new[] { "a", "b", "c" })]
        [InlineData("?a=1&c=3&b=2", new[] { "a", "b", "c" })]
        [InlineData("?b=2&a=1&c=3", new[] { "a", "b", "c" })]
        [InlineData("?b=2&c=3&a=1", new[] { "a", "b", "c" })]
        [InlineData("?c=3&a=1&b=2", new[] { "a", "b", "c" })]
        [InlineData("?c=3&b=2&a=1", new[] { "a", "b", "c" })]
        [InlineData("?A=1&B=2&C=3", new[] { "A", "B", "C" })]
        [InlineData("?A=1&C=3&B=2", new[] { "A", "B", "C" })]
        [InlineData("?B=2&A=1&C=3", new[] { "A", "B", "C" })]
        [InlineData("?B=2&C=3&A=1", new[] { "A", "B", "C" })]
        [InlineData("?C=3&A=1&B=2", new[] { "A", "B", "C" })]
        [InlineData("?C=3&B=2&A=1", new[] { "A", "B", "C" })]
        // Upper case characters have a lower code point than lower case characters
        [InlineData("?a=1&B=2&C=3", new[] { "B", "C", "a" })]
        public void SortQueryParameterNames(string query, string[] expected)
        {
            // Act
            var actual = CanonicalRequest.SortQueryParameters(query);

            // Assert
            actual.Keys.ShouldBe(expected);
        }

        [Theory]
        [InlineData("?a=1&a=2&a=3", "a", new[] { "1", "2", "3" })]
        [InlineData("?a=1&a=3&a=2", "a", new[] { "1", "2", "3" })]
        [InlineData("?a=2&a=1&a=3", "a", new[] { "1", "2", "3" })]
        [InlineData("?A=1&A=2&A=3", "A", new[] { "1", "2", "3" })]
        [InlineData("?A=1&A=3&A=2", "A", new[] { "1", "2", "3" })]
        [InlineData("?A=2&A=1&A=3", "A", new[] { "1", "2", "3" })]
        [InlineData("?a=1&b=10&a=3&b=13&a=2&b=12", "a", new[] { "1", "2", "3" })]
        public void SortQueryParameterValues(string query, string parameterName, string[] expected)
        {
            // Act
            var actual = CanonicalRequest.SortQueryParameters(query);

            // Assert
            actual[parameterName].ShouldBe(expected);
        }
    }
}