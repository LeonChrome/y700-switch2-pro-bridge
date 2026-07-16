using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Y700Switch2V55Manager;

internal sealed class SerialResponseFramer
{
    private readonly StringBuilder pending = new();

    public IEnumerable<string> Push(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) yield break;
        pending.Append(chunk.Replace("\r", ""));
        while (true)
        {
            int newline = IndexOfNewline();
            if (newline < 0) yield break;
            string line = pending.ToString(0, newline).Trim();
            pending.Remove(0, newline + 1);
            if (line.Length > 0) yield return line;
        }
    }

    public static bool TryMatchCommandResponse(string line, string command, out string json)
    {
        json = "";
        if (string.IsNullOrWhiteSpace(line) || line[0] != '{') return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("cmd", out JsonElement cmdElement)) return false;
            string? responseCommand = cmdElement.GetString();
            if (!CommandMatches(command, responseCommand)) return false;
            json = line;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private int IndexOfNewline()
    {
        for (int i = 0; i < pending.Length; i++)
        {
            if (pending[i] == '\n') return i;
        }
        return -1;
    }

    private static bool CommandMatches(string request, string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return false;
        string normalized = request.Trim();
        if (normalized.Equals(response, StringComparison.OrdinalIgnoreCase)) return true;
        if (normalized.StartsWith(response + " ", StringComparison.OrdinalIgnoreCase)) return true;
        return normalized switch
        {
            "ble auto on" or "ble auto off" => response.Equals("ble auto", StringComparison.OrdinalIgnoreCase),
            "status lite" => response.Equals("status", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
