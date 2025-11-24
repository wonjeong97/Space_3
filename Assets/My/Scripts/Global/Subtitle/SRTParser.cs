using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SRTParser
{
    private readonly List<SubtitleBlock> _subtitles;

    public SRTParser(string textAssetResourcePath)
    {
        TextAsset text = Resources.Load<TextAsset>(textAssetResourcePath);
        _subtitles = Load(text);
    }

    public SRTParser(TextAsset textAsset)
    {
        _subtitles = Load(textAsset);
    }

    ///<Summary>StreamingAssets 기준 상대 경로로부터 SRT 파서를 생성</Summary>
    public static SRTParser CreateFromStreamingAssets(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            Debug.LogError("[SRTParser] relativePath is null or empty.");
            return null;
        }

        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);

        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[SRTParser] Subtitle file not found -> {fullPath}");
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(fullPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SRTParser] Failed to read subtitle file -> {fullPath}\n{e}");
            return null;
        }

        List<SubtitleBlock> subs = LoadFromString(text);
        return new SRTParser(subs);
    }

    // 내부용 생성자
    private SRTParser(List<SubtitleBlock> subs)
    {
        _subtitles = subs ?? new List<SubtitleBlock>();
    }

    public static List<SubtitleBlock> Load(TextAsset textAsset)
    {
        if (textAsset == null)
        {
            Debug.LogError("Subtitle file is null");
            return new List<SubtitleBlock>();
        }

        return LoadFromString(textAsset.text);
    }

    // 순수 문자열에서 SRT 파싱
    private static List<SubtitleBlock> LoadFromString(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Debug.LogError("[SRTParser] Subtitle text is null or empty");
            return new List<SubtitleBlock>();
        }

        string[] lines = text.Split(
            new[] { "\r\n", "\r", "\n" },
            StringSplitOptions.None
        );

        var subs = new List<SubtitleBlock>();

        int currentIndex = 0;
        double currentFrom = 0;
        double currentTo = 0;
        string currentText = string.Empty;
        eReadState currentState = eReadState.Index;

        for (int l = 0; l < lines.Length; l++)
        {
            string line = lines[l];

            switch (currentState)
            {
                case eReadState.Index:
                {
                    int index;
                    if (int.TryParse(line, out index))
                    {
                        currentIndex = index;
                        currentState = eReadState.Time;
                    }
                    break;
                }

                case eReadState.Time:
                {
                    line = line.Replace(',', '.');
                    string[] parts = line.Split(
                        new[] { "-->" },
                        StringSplitOptions.RemoveEmptyEntries
                    );

                    if (parts.Length == 2)
                    {
                        TimeSpan fromTime;
                        TimeSpan toTime;
                        if (TimeSpan.TryParse(parts[0], out fromTime) &&
                            TimeSpan.TryParse(parts[1], out toTime))
                        {
                            currentFrom = fromTime.TotalSeconds;
                            currentTo = toTime.TotalSeconds;
                            currentState = eReadState.Text;
                        }
                    }
                    break;
                }

                case eReadState.Text:
                {
                    if (currentText != string.Empty)
                    {
                        currentText += "\r\n";
                    }

                    currentText += line;

                    // 빈 줄이 나오거나 마지막 줄이면 블록 종료
                    if (string.IsNullOrEmpty(line) || l == lines.Length - 1)
                    {
                        subs.Add(new SubtitleBlock(currentIndex, currentFrom, currentTo, currentText));

                        currentText = string.Empty;
                        currentState = eReadState.Index;
                    }

                    break;
                }
            }
        }

        return subs;
    }

    public SubtitleBlock GetForTime(float time)
    {
        if (_subtitles.Count > 0)
        {
            SubtitleBlock subtitle = _subtitles[0];

            // 현재 블록 끝을 지나쳤으면 버리고 다음 블록으로
            if (time >= subtitle.To)
            {
                _subtitles.RemoveAt(0);

                if (_subtitles.Count == 0)
                {
                    return null;
                }

                subtitle = _subtitles[0];
            }

            // 아직 자막 시작 전이면 Blank 반환
            if (subtitle.From > time)
            {
                return SubtitleBlock.Blank;
            }

            return subtitle;
        }

        return null;
    }

    private enum eReadState
    {
        Index,
        Time,
        Text
    }
}

public class SubtitleBlock
{
    private static SubtitleBlock _blank;

    public static SubtitleBlock Blank
        => _blank ?? (_blank = new SubtitleBlock(0, 0, 0, string.Empty));

    public int Index { get; }
    public double Length { get; }
    public double From { get; }
    public double To { get; }
    public string Text { get; }

    public SubtitleBlock(int index, double from, double to, string text)
    {
        Index = index;
        From = from;
        To = to;
        Length = to - from;
        Text = text;
    }
}
