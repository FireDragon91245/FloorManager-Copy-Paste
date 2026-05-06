namespace FloorManagerCopyPaste.Models;

internal sealed class TemplatePriceEstimate
{
    public int DeviceBase { get; set; }
    public int DeviceAdjusted { get; set; }
    public float CableLength { get; set; }
    public int CablePrice { get; set; }
    public int SfpCount { get; set; }
    public int SfpPrice { get; set; }
    public int Total => DeviceAdjusted + CablePrice + SfpPrice;
}