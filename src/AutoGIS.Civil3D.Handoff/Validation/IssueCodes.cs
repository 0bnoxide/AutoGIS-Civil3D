namespace AutoGIS.Civil3D.Handoff.Validation;

public static class IssueCodes
{
    public const string InvalidArchive = "ZIP001";
    public const string EntryCountMismatch = "ZIP002";
    public const string MissingRequiredEntry = "ZIP003";
    public const string UnexpectedEntry = "ZIP004";
    public const string UnsafeEntryName = "ZIP005";
    public const string DuplicateEntryName = "ZIP006";
    public const string NonRegularEntry = "ZIP007";
    public const string EncryptedEntry = "ZIP008";
    public const string UnsupportedCompression = "ZIP009";
    public const string ManifestTooLarge = "ZIP010";
    public const string SurfaceTooLarge = "ZIP011";
    public const string CompressionRatioExceeded = "ZIP012";
    public const string StreamLimitExceeded = "ZIP013";
    public const string LandXmlMalformed = "XML001";
    public const string LandXmlForbiddenDtd = "XML002";
    public const string LandXmlUnsupportedVersion = "XML003";
    public const string LandXmlInvalidSurfaceCount = "XML004";
    public const string LandXmlInvalidDefinitionCount = "XML005";
    public const string LandXmlInvalidPoint = "XML006";
    public const string LandXmlDuplicatePointId = "XML007";
    public const string LandXmlNonfiniteCoordinate = "XML008";
    public const string LandXmlInvalidFace = "XML009";
    public const string LandXmlUnknownPointReference = "XML010";
    public const string LandXmlRepeatedFaceVertex = "XML011";
    public const string LandXmlDegenerateFace = "XML012";
    public const string ManifestInvalidJson = "MAN001";
    public const string ManifestSchemaViolation = "MAN002";
    public const string ManifestSemanticViolation = "MAN003";
    public const string UnknownVerticalDatum = "WRN001";
}
