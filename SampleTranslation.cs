using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace WKTranslator;

public static class SampleTranslation
{
    private class SampleEntry
    {
        public string OriginalKey;
        public Regex CompiledRegex;
        public string TranslationTemplate;
        public List<string> Placeholders;
        public bool IsAtomic;
        public bool IsLiteral;
        public string Anchor;
    }

    private class GroupEntry
    {
        public List<string> Headers = new();
        public List<SampleEntry> Rules = new();
    }

    private class ConditionalEntry
    {
        public int RequiredGroupCount;
        public List<SampleEntry> MandatoryRules = new();
        public Dictionary<int, List<SampleEntry>> IndexedRules = new();
    }

    private static readonly List<SampleEntry> _samples = new();
    private static readonly List<GroupEntry> _groups = new();
    private static readonly List<ConditionalEntry> _conditionals = new();

    public static int Count => _samples.Count + _groups.Count + _conditionals.Count;

    public static void Clear()
    {
        _samples.Clear();
        _groups.Clear();
        _conditionals.Clear();
    }

    public static bool TryRegisterToken(string keyName, JToken token)
    {
        if (keyName.StartsWith("sampleR", StringComparison.OrdinalIgnoreCase) && token.Type == JTokenType.Object)
        {
            RegisterConditional(keyName, (JObject)token);
            return true;
        }

        if (keyName.StartsWith("sampleG:", StringComparison.OrdinalIgnoreCase) && token.Type == JTokenType.Object)
        {
            RegisterGroup((JObject)token);
            return true;
        }

        if (keyName.StartsWith("sample:", StringComparison.OrdinalIgnoreCase) && token.Type == JTokenType.Object)
        {
            foreach (var sub in ((JObject)token).Properties())
                _samples.Add(CreateEntry(sub.Name, sub.Value.ToString()));
            return true;
        }

        if (token.Type == JTokenType.String)
        {
            var val = token.ToString();
            if (val.StartsWith("sample:", StringComparison.OrdinalIgnoreCase))
            {
                _samples.Add(CreateEntry(keyName, val.Substring(7).Trim()));
                return true;
            }
        }

        return false;
    }

    public static void FinalizeRegistration()
    {
        _samples.Sort((a, b) =>
        {
            if (a.IsAtomic != b.IsAtomic) return a.IsAtomic.CompareTo(b.IsAtomic);
            return b.TranslationTemplate.Length.CompareTo(a.TranslationTemplate.Length);
        });
    }

    public static bool TryTranslate(string original, out string translated)
    {
        translated = null;
        if (string.IsNullOrEmpty(original)) return false;

        var workingText = original;
        var anyTranslated = false;

        foreach (var entry in _conditionals)
        {
            var mandatoryPassed = true;
            foreach (var rule in entry.MandatoryRules)
            {
                var match = rule.IsLiteral
                    ? workingText.IndexOf(rule.OriginalKey, StringComparison.OrdinalIgnoreCase) != -1
                    : rule.CompiledRegex.IsMatch(workingText);

                if (!match) { mandatoryPassed = false; break; }
            }

            if (!mandatoryPassed) continue;

            var matchedGroups = 0;
            var toApply = new List<SampleEntry>();

            foreach (var kvp in entry.IndexedRules)
            {
                var groupMatched = false;
                foreach (var rule in kvp.Value)
                {
                    var match = rule.IsLiteral
                        ? workingText.IndexOf(rule.OriginalKey, StringComparison.OrdinalIgnoreCase) != -1
                        : rule.CompiledRegex.IsMatch(workingText);

                    if (match)
                    {
                        groupMatched = true;
                        toApply.Add(rule);
                    }
                }
                if (groupMatched) matchedGroups++;
            }

            if (matchedGroups < entry.RequiredGroupCount) continue;

            foreach (var rule in entry.MandatoryRules)
                workingText = ApplyReplacement(workingText, rule, ref anyTranslated);

            toApply.Sort((a, b) => b.OriginalKey.Length.CompareTo(a.OriginalKey.Length));

            foreach (var rule in toApply)
                workingText = ApplyReplacement(workingText, rule, ref anyTranslated);
        }

        foreach (var group in _groups)
        {
            if (group.Headers.Count == 0) continue;

            var headerFound = false;
            foreach (var header in group.Headers)
            {
                if (workingText.IndexOf(header, StringComparison.OrdinalIgnoreCase) == -1) continue;
                headerFound = true;
                break;
            }
            if (!headerFound) continue;

            foreach (var rule in group.Rules)
                workingText = ApplyReplacement(workingText, rule, ref anyTranslated);
        }

        foreach (var sample in _samples)
        {
            if (!string.IsNullOrEmpty(sample.Anchor) && workingText.IndexOf(sample.Anchor, StringComparison.OrdinalIgnoreCase) == -1)
                continue;

            workingText = ApplyReplacement(workingText, sample, ref anyTranslated);
        }

        if (!anyTranslated) return false;

        translated = workingText;
        return true;
    }

    private static void RegisterConditional(string keyName, JObject obj)
    {
        var entry = new ConditionalEntry();

        var countMatch = Regex.Match(keyName, @"\d+");
        entry.RequiredGroupCount = countMatch.Success ? int.Parse(countMatch.Value) : 2;

        foreach (var sub in obj.Properties())
        {
            var name = sub.Name;
            var val = sub.Value.ToString();

            if (name.StartsWith("request:", StringComparison.OrdinalIgnoreCase))
            {
                var trigger = name.Substring(8);
                entry.MandatoryRules.Add(CreateEntry(trigger, val));
                continue;
            }

            var reqMatch = Regex.Match(name, @"^request(\d+):(.*)", RegexOptions.IgnoreCase);
            if (!reqMatch.Success) continue;

            var groupId = int.Parse(reqMatch.Groups[1].Value);
            var trigger2 = reqMatch.Groups[2].Value;
            var rule = CreateEntry(trigger2, val);

            if (!entry.IndexedRules.TryGetValue(groupId, out var list))
            {
                list = new List<SampleEntry>();
                entry.IndexedRules[groupId] = list;
            }
            list.Add(rule);
        }

        _conditionals.Add(entry);
    }

    private static void RegisterGroup(JObject obj)
    {
        var group = new GroupEntry();

        foreach (var sub in obj.Properties())
        {
            if (sub.Name.StartsWith("header:", StringComparison.OrdinalIgnoreCase))
            {
                var trigger = sub.Name.Substring(7);
                var headerCheck = trigger.StartsWith("tag:", StringComparison.OrdinalIgnoreCase) ? trigger.Substring(4) : trigger;
                group.Headers.Add(headerCheck);

                var val = sub.Value.ToString();
                if (!string.IsNullOrEmpty(val))
                    group.Rules.Add(CreateEntry(trigger, val));
            }
            else
            {
                group.Rules.Add(CreateEntry(sub.Name, sub.Value.ToString()));
            }
        }

        _groups.Add(group);
    }

    private static SampleEntry CreateEntry(string key, string val)
    {
        var exactKey = key;
        var isLiteral = false;

        if (exactKey.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        {
            isLiteral = true;
            exactKey = exactKey.Substring(4);
        }

        var placeholders = new List<string>();
        foreach (Match m in Regex.Matches(exactKey, @"\{(\d+)\}"))
            placeholders.Add(m.Groups[1].Value);

        var isAtomic = placeholders.Count == 0;
        var pattern = Regex.Escape(exactKey);
        pattern = Regex.Replace(pattern, @"\\\{\d+\}", @"(.+?)");

        if (isAtomic && !isLiteral)
        {
            var prefix = Regex.IsMatch(exactKey, @"^\w") ? @"(?<=^|\W|\\n|\\r)" : "";
            var suffix = Regex.IsMatch(exactKey, @"\w$") ? @"(?=\W|$)" : "";
            pattern = prefix + pattern + suffix;
        }

        return new SampleEntry
        {
            OriginalKey = exactKey,
            CompiledRegex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            TranslationTemplate = val,
            Placeholders = placeholders,
            IsAtomic = isAtomic,
            IsLiteral = isLiteral,
            Anchor = GetLongestStaticChunk(exactKey)
        };
    }

    private static string ApplyReplacement(string input, SampleEntry sample, ref bool anyTranslated)
    {
        if (sample.IsLiteral)
        {
            if (input.IndexOf(sample.OriginalKey, StringComparison.OrdinalIgnoreCase) == -1) return input;

            var result = Regex.Replace(input, Regex.Escape(sample.OriginalKey), sample.TranslationTemplate.Replace("$", "$$"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (result == input) return input;

            anyTranslated = true;
            return result;
        }

        if (sample.IsAtomic)
        {
            if (input.IndexOf(sample.OriginalKey, StringComparison.OrdinalIgnoreCase) == -1) return input;

            var result = sample.CompiledRegex.Replace(input, m =>
            {
                var searchStart = m.Index - 1;
                if (searchStart >= 0 && searchStart < input.Length)
                {
                    var openTag = input.LastIndexOf('<', searchStart);
                    var closeTag = input.LastIndexOf('>', searchStart);
                    if (openTag > closeTag) return m.Value;

                    var openBrace = input.LastIndexOf('{', searchStart);
                    var closeBrace = input.LastIndexOf('}', searchStart);
                    if (openBrace > closeBrace) return m.Value;
                }

                return sample.TranslationTemplate;
            });

            if (result == input) return input;

            anyTranslated = true;
            return result;
        }

        if (!sample.CompiledRegex.IsMatch(input)) return input;

        var replaced = sample.CompiledRegex.Replace(input, match =>
        {
            var translatedSample = sample.TranslationTemplate;
            for (var g = 1; g < match.Groups.Count; g++)
            {
                var placeholderName = sample.Placeholders[g - 1];
                var capturedValue = match.Groups[g].Value.Trim();
                translatedSample = translatedSample.Replace("{" + placeholderName + "}", capturedValue);
            }
            return translatedSample;
        });

        anyTranslated = true;
        return replaced;
    }

    private static string GetLongestStaticChunk(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var clean = Regex.Replace(input, @"<[^>]+>", " ");
        clean = Regex.Replace(clean, @"\{\#?\d+\}", " ");

        var chunks = clean.Split(new[] { ' ', '\t', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        var longest = string.Empty;
        foreach (var chunk in chunks)
        {
            if (chunk.Length > longest.Length) longest = chunk;
        }

        return longest.Length > 3 ? longest : string.Empty;
    }
}