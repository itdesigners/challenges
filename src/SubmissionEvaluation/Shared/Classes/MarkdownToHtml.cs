using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Parsers;
using Markdig.Renderers;
using SubmissionEvaluation.Shared.Classes.Config;

namespace SubmissionEvaluation.Shared.Classes
{
    public static class MarkdownToHtml
    {
        public static string Convert(string markdown, string relativeUrl = null, bool consoleParsing = true)
        {
            var containsHtmlParagraphs = markdown.Contains("<p>");
            if (relativeUrl == null)
            {
                relativeUrl = $"{Settings.Application.SiteUrl}/";
            }

            string FixLink(string x)
            {
                if (x.StartsWith("~/"))
                {
                    return Settings.Application.SiteUrl + x.Substring(1);
                }

                if (x.StartsWith("/"))
                {
                    return Settings.Application.SiteUrl + x;
                }

                if (!Uri.IsWellFormedUriString(x, UriKind.Absolute))
                {
                    return relativeUrl + x;
                }

                return x;
            }

            if (string.IsNullOrWhiteSpace(markdown))
            {
                return string.Empty;
            }

            var result = new StringWriter();

            var pipeline = new MarkdownPipelineBuilder().UseAutoLinks().UsePipeTables(new PipeTableOptions {RequireHeaderSeparator = false}).Build();
            var renderer = new HtmlRenderer(result) {LinkRewriter = FixLink};
            pipeline.Setup(renderer);

            var tempMarkdown = new StringBuilder();
            using (var reader = new StringReader(markdown))
            {
                var insideCode = false;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var matchBegin = Regex.Match(line, @"\{%\s*output\s*%\}");
                    if (matchBegin.Success)
                    {
                        tempMarkdown.Append(line.Substring(0, matchBegin.Index));
                        renderer.Render(MarkdownParser.Parse(tempMarkdown.ToString(), pipeline));
                        tempMarkdown.Clear();

                        if (consoleParsing)
                        {
                            line = line.Substring(matchBegin.Index + matchBegin.Length);
                        }
                        else
                        {
                            line = line.Substring(matchBegin.Index);
                        }

                        insideCode = true;
                        if (consoleParsing)
                        {
                            result.Write("<div><pre class=\'consoleblock\'>");
                        }
                    }

                    if (insideCode)
                    {
                        if (!consoleParsing & !containsHtmlParagraphs)
                        {
                            result.Write("<p>");
                        }

                        var matchEnd = Regex.Match(line, @"\{%\s*endoutput\s*%\}(?<realtrailing>.(?<trailing>.)*)*");
                        if (matchEnd.Success)
                        {
                            if (consoleParsing)
                            {
                                line = line.Substring(0, matchEnd.Index);
                            }

                            insideCode = false;
                        }

                        if (line.StartsWith(">"))
                        {
                            line = $"<span class=\"console_start\">{line}</span>";
                        }

                        if (consoleParsing)
                        {
                            line = Regex.Replace(line, @"`\[([A-F0-9]{6})\]([^`]+)`", "<span style=\"color:#$1\">$2</span>");
                            line = Regex.Replace(line, @"`([^`]+)`", "<span class=\"console_input\">$1</span>");
                        }

                        if (matchEnd.Success)
                        {
                            result.Write(line);
                            if (consoleParsing)
                            {
                                result.Write("</pre></div>");
                                tempMarkdown.AppendLine(matchEnd.Groups["realtrailing"].Value);
                            }
                            else if (!containsHtmlParagraphs)
                            {
                                result.Write("</p>");
                            }
                        }
                        else
                        {
                            if (consoleParsing)
                            {
                                result.WriteLine(line);
                            }
                            else
                            {
                                result.Write(line);
                                if (!containsHtmlParagraphs)
                                {
                                    result.Write("</p>");
                                }

                                result.WriteLine("");
                            }
                        }
                    }
                    else
                    {
                        tempMarkdown.AppendLine(line);
                    }
                }
            }

            renderer.Render(MarkdownParser.Parse(tempMarkdown.ToString(), pipeline));
            result.Flush();
            if (consoleParsing)
            {
                return ReplaceBulletLists(result.ToString());
            }
            else
            {
                return result.ToString();
            }
        }

        /**
         * Since quill represents uls with styled ols, they need to be replaced.
         */
        private static string ReplaceBulletLists(string input)
        {
            var result = new StringBuilder();

            using (var reader = new StringReader(input))
            {
                string line;
                var inUl = false;
                while ((line = reader.ReadLine()) != null)
                {
                    var containsOrderedList = Regex.Match(line, @"<\s*ol\s*>");
                    var containsOrderedListEnd = Regex.Match(line, @"<\s*/ol\s*>");
                    if (containsOrderedList.Success)
                    {
                        var containsBulletPoints = Regex.Match(line, @"<li\s+data-list=\s*""bullet""\s*>");
                        if (containsBulletPoints.Success)
                        {
                            line = line.Substring(0, containsOrderedList.Index) + "<ul>" +
                                   line.Substring(containsOrderedList.Index + containsOrderedList.Length);
                            inUl = true;
                        }
                        else
                        {
                            var nextLine = reader.ReadLine();
                            if (nextLine != null)
                            {
                                var nextContainsBulletPoints = Regex.Match(nextLine, @"<li\s+data-list=\s*""bullet""\s*>");
                                var nextContainsOrderedListEnd = Regex.Match(nextLine, @"<\s*/ol\s*>");
                                if (nextContainsBulletPoints.Success)
                                {
                                    line = line.Substring(0, containsOrderedList.Index) + "<ul>" +
                                           line.Substring(containsOrderedList.Index + containsOrderedList.Length);
                                    inUl = true;
                                    if (nextContainsOrderedListEnd.Success)
                                    {
                                        inUl = false;
                                        nextLine = nextLine.Substring(0, nextContainsOrderedListEnd.Index) + "</ul>" +
                                                   nextLine.Substring(nextContainsOrderedListEnd.Index + nextContainsOrderedListEnd.Length);
                                    }
                                }

                                result.Append(line + "\n");
                                result.Append(nextLine + "\n");
                                continue;
                            }
                        }

                        if (containsOrderedListEnd.Success && inUl)
                        {
                            line = line.Substring(0, containsOrderedListEnd.Index) + "</ul>" +
                                   line.Substring(containsOrderedListEnd.Index + containsOrderedListEnd.Length);
                            inUl = false;
                        }

                        result.Append(line + "\n");
                    }
                    else if (containsOrderedListEnd.Success && inUl)
                    {
                        line = line.Substring(0, containsOrderedListEnd.Index) + "</ul>" +
                               line.Substring(containsOrderedListEnd.Index + containsOrderedListEnd.Length);
                        inUl = false;
                        result.Append(line + "\n");
                    }
                    else
                    {
                        result.Append(line + "\n");
                    }
                }
            }

            return result.ToString().Trim();
        }
    }
}
