    static string FormatDeliveredAdditions(string intended, string delivered, string marker) {
        if (string.IsNullOrWhiteSpace(intended) || intended == "...") return $"{marker}{delivered}{marker}";
        
        int exactIdx = delivered.IndexOf(intended, StringComparison.OrdinalIgnoreCase);
        if (exactIdx >= 0) return WrapAdditions(intended, delivered, exactIdx, marker);
        
        string normalized = Regex.Replace(intended, @"\s+", " ");
        string pattern = Regex.Escape(normalized).Replace(@"\ ", @"\s+");
        var match = Regex.Match(delivered, pattern, RegexOptions.IgnoreCase);
        if (match.Success) {
            return WrapAdditions(match.Value, delivered, match.Index, marker);
        }
        
        return $"{marker}{delivered}{marker}";
    }
    
    static string WrapAdditions(string matchStr, string delivered, int exactIdx, string marker) {
        string before = delivered.Substring(0, exactIdx);
        string after = delivered.Substring(exactIdx + matchStr.Length);
        
        string afterSpace = "";
        while (after.Length > 0 && (char.IsWhiteSpace(after[0]) || char.IsPunctuation(after[0]))) {
            afterSpace += after[0];
            after = after.Substring(1);
        }
        
        string beforeSpace = "";
        while (before.Length > 0 && (char.IsWhiteSpace(before[before.Length - 1]) || char.IsPunctuation(before[before.Length - 1]))) {
            beforeSpace = before[before.Length - 1] + beforeSpace;
            before = before.Substring(0, before.Length - 1);
        }
        
        string res = "";
        if (before.Length > 0) res += marker + before + marker + beforeSpace;
        else res += beforeSpace;
        
        res += matchStr;
        
        if (after.Length > 0) res += afterSpace + marker + after + marker;
        else res += afterSpace;
        
        return res;
    }
