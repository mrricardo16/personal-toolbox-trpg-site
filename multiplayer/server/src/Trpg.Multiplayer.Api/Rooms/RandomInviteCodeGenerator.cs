using System.Security.Cryptography;

namespace Trpg.Multiplayer.Api.Rooms;

public sealed class RandomInviteCodeGenerator : IInviteCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 7;

    public string Generate()
    {
        var characters = new char[CodeLength];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(characters);
    }
}
