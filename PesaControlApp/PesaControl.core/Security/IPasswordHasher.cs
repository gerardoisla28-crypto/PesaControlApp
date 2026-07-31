namespace PesaControl.Core.Security;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encodedHash);
    bool NeedsRehash(string encodedHash);
}
