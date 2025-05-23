using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

public static class CertificateAuthority
{
    private static string cn = "";
    private static readonly ConcurrentDictionary<string, X509Certificate2> LeafCertCache = new();
    public static X509Certificate2 RootCert { get; private set; }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
    private static void InstallCertificate(X509Certificate2 cert, StoreLocation location)
    {
        using var store = new X509Store(StoreName.Root, location);
        store.Open(OpenFlags.ReadWrite);
        store.Add(cert);
        store.Close();

        Console.WriteLine($"[*] Root CA added to {location}\\Root.");
    }

    public static void SetupRootCertificate()
    {
        RootCert = GenerateRootCA();

        if (IsRunningAsAdministrator())
        {
            Console.WriteLine("[!] Privileged user - Installation in machine store.");
            InstallCertificate(RootCert, StoreLocation.LocalMachine);
        }
        else
        {
            Console.WriteLine("[!] Unprivileged user - Installation in user store.");
            InstallCertificate(RootCert, StoreLocation.CurrentUser);
        }
    }

    static string Rnd(int length = 8)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var rng = RandomNumberGenerator.Create();
        var bytes = new byte[length];
        rng.GetBytes(bytes);
        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = chars[bytes[i] % chars.Length];
        }
        return new string(result);
    }

    private static X509Certificate2 GenerateRootCA()
    {
        using var rsa = RSA.Create(2048);
        cn = Rnd();
        var req = new CertificateRequest("CN=" + cn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));

        return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string?)null,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
    }

    public static X509Certificate2 GenerateCertificateForHost(string hostname)
    {
        if (LeafCertCache.TryGetValue(hostname, out var cachedCert))
        {
            return cachedCert;
        }

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={hostname}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        // Add san with hostname
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(hostname);
        req.CertificateExtensions.Add(sanBuilder.Build());

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(1);

        var cert = req.Create(RootCert, notBefore, notAfter, Guid.NewGuid().ToByteArray());

        string tempPassword = Rnd();
        var pfx = cert.CopyWithPrivateKey(rsa).Export(X509ContentType.Pfx, tempPassword);
        var certWithPrivateKey = new X509Certificate2(pfx, tempPassword,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);

        // Console.WriteLine($"[DEBUG] Generated cert for {hostname}, HasPrivateKey = {certWithPrivateKey.HasPrivateKey}");

        LeafCertCache.TryAdd(hostname, certWithPrivateKey);
        return certWithPrivateKey;
    }
    public static void CleanupCertificates()
    {
        RemoveFromStore(StoreLocation.CurrentUser);
        if (IsRunningAsAdministrator())
            RemoveFromStore(StoreLocation.LocalMachine);
    }

    private static void RemoveFromStore(StoreLocation location)
    {
        using var store = new X509Store(StoreName.Root, location);
        store.Open(OpenFlags.ReadWrite);

        var certs = store.Certificates.Find(X509FindType.FindBySubjectName, cn, false);
        foreach (var cert in certs)
        {
            try
            {
                store.Remove(cert);
                Console.WriteLine($"[*] Removed certificate from {location}: {cert.Subject}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Error: {ex.Message}");
            }
        }

        store.Close();
    }
}
