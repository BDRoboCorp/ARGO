using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Extensions;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Retrieves email content associated with the requested record.
    /// </summary>
    public class GetEmailContentApi : PluginBase
    {
        public readonly string FetchXmlKey = "FetchXml";
        public readonly string TemplateNameKey = "TempateName";
        public readonly string StripHtmlKey = "StripHtml";
        public readonly string OutputKey = "Result";

        public GetEmailContentApi() : base(typeof(GetEmailContentApi))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var fetchXml = localPluginContext.PluginExecutionContext.InputParameterOrDefault<string>(FetchXmlKey);
            if (string.IsNullOrEmpty(fetchXml)) throw new ArgumentNullException(FetchXmlKey);

            var templateName = localPluginContext.PluginExecutionContext.InputParameterOrDefault<string>(TemplateNameKey);
            if (string.IsNullOrEmpty(fetchXml)) throw new ArgumentNullException(TemplateNameKey);

            var isStripHtml = localPluginContext.PluginExecutionContext.InputParameters.ContainsKey(StripHtmlKey) ?
                localPluginContext.PluginExecutionContext.InputParameterOrDefault<bool>(StripHtmlKey) : false;

            var template = GetTemplate(localPluginContext.AdminService, templateName);
            if (template == null) throw new ArgumentNullException(templateName);

            var data = localPluginContext.AdminService.RetrieveMultiple(new FetchExpression(fetchXml)).Entities.ToArray();
            var firstData = data.FirstOrDefault() ?? new Entity();
            var subject = GetTemplateContent(ReplaceText(template.GetAttributeValue<string>("subject"), firstData));
            var bodyText = GetTemplateContent(ProcessList(template.GetAttributeValue<string>("body"), data));
            var body = ReplaceText(bodyText, firstData);

            localPluginContext.PluginExecutionContext.OutputParameters[OutputKey] = JsonSerializer.Serialize(new GetEmailContentResult
            {
                Subject = StripHtml(subject),
                Body = isStripHtml ? StripHtml(body) : body
            });
        }

        private static string StripHtml(string text)
        {
            return Regex.Replace(text, "<.*?>", String.Empty);
        }

        private static string GetTemplateContent(string text)
        {
            var valid = text.Contains("xml") && text.Contains("xsl:template match");
            if (!valid) return text;
            var document = new XmlDocument();
            document.LoadXml(text);
            var templates = document.GetElementsByTagName("xsl:template");
            return templates.Count > 0 ? templates[0].InnerText : "";
        }

        private static string ProcessList(string text, Entity[] entities)
        {
            var currentIndex = text.IndexOf("<list>", StringComparison.Ordinal);
            var lastIndex = text.IndexOf("</list>", StringComparison.Ordinal);
            var valid = currentIndex > -1 && lastIndex > -1;
            if (valid)
            {
                currentIndex += 6;
                var originalText = text.Substring(currentIndex, lastIndex - currentIndex);
                var result = new List<string>();
                foreach (var entity in entities)
                {
                    var tempText = string.Copy(originalText);
                    result.Add(ReplaceText(tempText, entity));
                }
                text = (text.Substring(0, currentIndex) + string.Join(" ", result) + text.Substring(lastIndex))
                    .Replace("<list>", "")
                    .Replace("</list>", "");
            }

            var rowPattern = @"<tr\b(?=[^>]*\s+id\s*=\s*(?:""list""|'list'))[^>]*>";
            var rowTags = new Regex(@"</?tr\b[^>]*>", RegexOptions.IgnoreCase);
            var searchIndex = 0;
            while (searchIndex < text.Length)
            {
                var row = Regex.Match(text.Substring(searchIndex), rowPattern, RegexOptions.IgnoreCase);
                if (!row.Success) break;

                var rowStart = searchIndex + row.Index;
                var contentStart = rowStart + row.Length;
                var depth = 1;
                Match closingRow = null;
                // Nested tables have their own rows; skip those closing tags.
                for (var tag = rowTags.Match(text, contentStart); tag.Success; tag = tag.NextMatch())
                {
                    depth += tag.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
                    if (depth == 0)
                    {
                        closingRow = tag;
                        break;
                    }
                }

                if (closingRow == null)
                {
                    throw new InvalidPluginExecutionException("Email template contains a <tr id=\"list\"> without a matching </tr>.");
                }

                var content = text.Substring(contentStart, closingRow.Index - contentStart);
                var rows = string.Join(" ", entities.Select(entity => "<tr>" + ReplaceText(content, entity) + "</tr>"));
                var rowEnd = closingRow.Index + closingRow.Length;
                text = text.Substring(0, rowStart) + rows + text.Substring(rowEnd);
                searchIndex = rowStart + rows.Length;
            }

            return text;
        }

        private static string ReplaceText(string text, Entity source)
        {
            foreach (Match match in Regex.Matches(text, "{[^{}]+}"))
            {
                var key = match.Value;
                var attributeName = GetAttributeName(key);
                var formatString = GetFormatString(key);
                var value = source.Attributes.Contains(attributeName) ? GetAttributeValueText(source, attributeName, source[attributeName], formatString) : string.Empty;
                text = text.Replace(key, value);
            }
            return text;
        }

        private static string GetAttributeValueText(Entity source, string attributeName, object value, string format)
        {
            if (string.IsNullOrEmpty(format) && source.FormattedValues.Contains(attributeName)) return source.FormattedValues[attributeName];
            if (value == null) return string.Empty;

            if (value is AliasedValue aliasedValue) return GetAttributeValueText(source, attributeName, aliasedValue.Value, format);
            if (value is OptionSetValue optionSetValue) return ToString(optionSetValue.Value, format);
            if (value is Money money) return ToString(money.Value, format);

            return ToString(value, format);
        }

        private static string ToString(object value, string format)
        {
            return string.IsNullOrEmpty(format) ? value.ToString() : string.Format("{0:" + format + "}", value);
        }

        private static string GetFormatString(string key)
        {
            var list = key.Replace("}", "").Split(':').ToArray();
            return list.Length > 1 ? list.LastOrDefault() : string.Empty;
        }

        private static string GetAttributeName(string key)
        {
            return key.Replace("{", "").Replace("}", "").Split(':').FirstOrDefault();
        }

        private static int FindCloseIndex(string text, int index)
        {
            for (var i = index; i < text.Length; i++)
            {
                var current = text[i];
                if (current == '}') return i + 1;
            }
            return -1;
        }

        private Entity GetTemplate(IOrganizationService service, string templateName)
        {
            var query = new QueryExpression("template")
            {
                ColumnSet = new ColumnSet("subject", "body"),
                TopCount = 1
            };
            query.Criteria.AddCondition("title", ConditionOperator.Equal, templateName);

            var result = service.RetrieveMultiple(query);

            return result.Entities.FirstOrDefault();
        }

        /// <summary>
        /// Contains the email content returned by the email-content operation.
        /// </summary>
        public class GetEmailContentResult
        {
            public string Subject { get; set; }
            public string Body { get; set; }
        }
    }
}
