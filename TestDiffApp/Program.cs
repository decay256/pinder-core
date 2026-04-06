using System;
using System.Text.RegularExpressions;

class Program {
    static void Main() {
        PrintQuoted("**Intended:** \"hello\nworld\"");
    }
    
    static void PrintQuoted(string text)
    {
        if (string.IsNullOrEmpty(text)) { Console.WriteLine("> (empty)"); return; }
        foreach (var line in text.Split('\n'))
        {
            Console.WriteLine(string.IsNullOrWhiteSpace(line) ? ">" : $"> {line.TrimEnd()}");
        }
    }
}
