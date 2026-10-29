using System.Text;

namespace Rabbit.Services;

/// <summary>
/// 한글 이름에서 초성을 뽑아 초성 검색을 할 수 있게 합니다("ㅎㄹ" → "헬로").
/// </summary>
public static class KoreanInitials
{
    private const string Consonants = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    private const int FirstSyllable = 0xAC00;
    private const int LastSyllable = 0xD7A3;
    private const int SyllablesPerInitial = 588;

    /// <summary>이름에서 초성만 뽑습니다. 한글이 아닌 글자는 그대로 두고 공백은 버립니다.</summary>
    public static string From(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (character is >= (char)FirstSyllable and <= (char)LastSyllable)
            {
                builder.Append(Consonants[(character - FirstSyllable) / SyllablesPerInitial]);
            }
            else if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>검색어가 자음으로만 이뤄졌는지 봅니다. 초성 검색인지 판단할 때 씁니다.</summary>
    public static bool IsConsonantOnly(string text)
    {
        var found = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            if (!Consonants.Contains(character))
            {
                return false;
            }

            found = true;
        }

        return found;
    }
}
