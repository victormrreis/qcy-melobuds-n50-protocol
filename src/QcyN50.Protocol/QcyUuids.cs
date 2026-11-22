namespace N50.Core.Protocol;

public static class QcyUuids
{
    public const ushort QcyCompanyId = 0x521C; // 21020

    // Serviço e características JieLi RCSP (confirmado em JLDeviceImpl.<clinit> e enumerado no Windows)
    public static readonly Guid JieLiService = new("0000A002-0000-1000-8000-00805F9B34FB");
    public static readonly Guid JieLiWrite = new("00000001-0000-1000-8000-00805F9B34FB");
    public static readonly Guid JieLiNotify = new("00000002-0000-1000-8000-00805F9B34FB");

    // Vendor IDs declarados para o QCY MeloBuds N50 (HT12)
    public const ushort VendorIdN50 = 0x477A;     // 18298
    public const ushort VendorIdHt12N50 = 0x477C; // 18300
}
