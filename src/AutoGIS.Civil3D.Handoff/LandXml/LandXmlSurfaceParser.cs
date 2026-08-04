using System.Globalization;
using System.Text;
using System.Xml;
using AutoGIS.Civil3D.Handoff.Manifest;
using AutoGIS.Civil3D.Handoff.Validation;

namespace AutoGIS.Civil3D.Handoff.LandXml;

internal static class LandXmlSurfaceParser
{
    private const string LandXmlNamespace = "http://www.landxml.org/schema/LandXML-1.2";
    private const string SupportedVersion = "1.2";
    private const int MaximumLeafTokenCount = 4;
    private const int MaximumScalarTokenLength = 128;
    private const int ValueChunkLength = 1024;

    internal static LandXmlParseResult Parse(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        ForbiddenSequenceReadStream scanningStream = new(xml);
        XmlReaderSettings settings = new()
        {
            Async = false,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersFromEntities = 0
        };

        try
        {
            using XmlReader reader = XmlReader.Create(scanningStream, settings);
            return Parse(reader);
        }
        catch (ForbiddenSequenceException)
        {
            return Invalid(IssueCodes.LandXmlForbiddenDtd, "LandXML document type declarations are forbidden.");
        }
        catch (XmlException exception) when (exception.InnerException is ForbiddenSequenceException)
        {
            return Invalid(IssueCodes.LandXmlForbiddenDtd, "LandXML document type declarations are forbidden.");
        }
        catch (XmlException) when (scanningStream.ForbiddenSequenceFound)
        {
            return Invalid(IssueCodes.LandXmlForbiddenDtd, "LandXML document type declarations are forbidden.");
        }
        catch (XmlException)
        {
            return Invalid(IssueCodes.LandXmlMalformed, "surface.landxml is not well-formed XML.");
        }
    }

    private static LandXmlParseResult Parse(XmlReader reader)
    {
        if (reader.MoveToContent() != XmlNodeType.Element)
        {
            return Invalid(IssueCodes.LandXmlMalformed, "surface.landxml does not contain a root element.");
        }

        bool envelopeInvalid =
            reader.Depth != 0 ||
            reader.LocalName != "LandXML" ||
            reader.NamespaceURI != LandXmlNamespace ||
            reader.GetAttribute("version") != SupportedVersion;

        List<ElementFrame> ancestors = [];
        Dictionary<long, Point3> points = [];
        List<ValidationIssue> issues = [];
        string? surfaceName = null;
        int epsgCode = 0;
        LinearUnit? horizontalUnit = null;
        VerticalUnitFamily? verticalUnitFamily = null;
        long surfaceCount = 0;
        long canonicalSurfaceCount = 0;
        long definitionCount = 0;
        long pointCount = 0;
        long faceCount = 0;
        int coordinateSystemCount = 0;
        int unitDeclarationCount = 0;
        bool definitionInvalid = false;
        bool surfaceInvalid = false;

        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                ElementFrame element = new(reader.LocalName, reader.NamespaceURI);

                if (IsLandXmlElement(element, "Metric") || IsLandXmlElement(element, "Imperial"))
                {
                    if (HasLandXmlAncestorPath(ancestors, "LandXML", "Units"))
                    {
                        unitDeclarationCount++;
                        ParseUnits(reader, ref horizontalUnit, ref verticalUnitFamily, ref envelopeInvalid);
                    }
                }
                else if (IsLandXmlElement(element, "CoordinateSystem"))
                {
                    if (HasLandXmlAncestorPath(ancestors, "LandXML"))
                    {
                        coordinateSystemCount++;
                        if (!TryParsePositiveInt32(reader.GetAttribute("epsgCode"), out epsgCode))
                        {
                            envelopeInvalid = true;
                        }
                    }
                }
                else if (IsLandXmlElement(element, "Surface"))
                {
                    surfaceCount++;
                    if (HasLandXmlAncestorPath(ancestors, "LandXML", "Surfaces"))
                    {
                        canonicalSurfaceCount++;
                        string? candidateName = reader.GetAttribute("name")?.Trim();
                        if (string.IsNullOrEmpty(candidateName))
                        {
                            surfaceInvalid = true;
                        }
                        else if (surfaceName is null)
                        {
                            surfaceName = candidateName;
                        }
                    }
                }
                else if (IsLandXmlElement(element, "Definition") &&
                    HasLandXmlAncestorPath(ancestors, "LandXML", "Surfaces", "Surface"))
                {
                    definitionCount++;
                    if (reader.GetAttribute("surfType") != "TIN")
                    {
                        definitionInvalid = true;
                    }
                }
                else if (IsLandXmlElement(element, "P") &&
                    HasLandXmlAncestorPath(
                        ancestors,
                        "LandXML",
                        "Surfaces",
                        "Surface",
                        "Definition",
                        "Pnts"))
                {
                    string? idText = reader.GetAttribute("id");
                    LeafContent pointContent = ReadLeafContent(reader);
                    surfaceCount += pointContent.SameNamespaceSurfaceCount;
                    if (pointContent.HasChildElement)
                    {
                        AddIssueOnce(issues, IssueCodes.LandXmlInvalidPoint, "A TIN point is malformed.");
                    }
                    else
                    {
                        ParsePoint(idText, pointContent, points, issues, ref pointCount);
                    }

                    continue;
                }
                else if (IsLandXmlElement(element, "F") &&
                    HasLandXmlAncestorPath(
                        ancestors,
                        "LandXML",
                        "Surfaces",
                        "Surface",
                        "Definition",
                        "Faces"))
                {
                    LeafContent faceContent = ReadLeafContent(reader);
                    surfaceCount += faceContent.SameNamespaceSurfaceCount;
                    faceCount++;
                    if (faceContent.HasChildElement)
                    {
                        AddIssueOnce(issues, IssueCodes.LandXmlInvalidFace, "A TIN face is malformed.");
                    }
                    else
                    {
                        ParseFace(faceContent, points, issues);
                    }

                    continue;
                }

                if (!reader.IsEmptyElement)
                {
                    ancestors.Add(element);
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (ancestors.Count == 0 ||
                    ancestors[^1].LocalName != reader.LocalName ||
                    ancestors[^1].NamespaceUri != reader.NamespaceURI)
                {
                    throw new XmlException("The LandXML element stack is inconsistent.");
                }

                ancestors.RemoveAt(ancestors.Count - 1);
            }

            reader.Read();
        }

        if (unitDeclarationCount != 1 || coordinateSystemCount != 1 ||
            horizontalUnit is null || verticalUnitFamily is null || epsgCode <= 0)
        {
            envelopeInvalid = true;
        }

        if (envelopeInvalid)
        {
            AddIssueOnce(issues, IssueCodes.LandXmlUnsupportedVersion, "LandXML must use the supported 1.2 envelope, units, and EPSG declaration.");
        }

        if (surfaceCount != 1 || canonicalSurfaceCount != 1 || surfaceInvalid || surfaceName is null)
        {
            AddIssueOnce(issues, IssueCodes.LandXmlInvalidSurfaceCount, "LandXML must contain exactly one named surface.");
        }

        if (definitionCount != 1 || definitionInvalid)
        {
            AddIssueOnce(issues, IssueCodes.LandXmlInvalidDefinitionCount, "The surface must contain exactly one TIN definition.");
        }

        if (pointCount == 0)
        {
            AddIssueOnce(issues, IssueCodes.LandXmlInvalidPoint, "The TIN definition must contain valid points.");
        }

        if (faceCount == 0)
        {
            AddIssueOnce(issues, IssueCodes.LandXmlInvalidFace, "The TIN definition must contain faces.");
        }

        if (issues.Count > 0)
        {
            // XML issue codes are numbered in validation-flow order, so the minimum
            // code is the root-cause defect; later codes co-occurring in the same
            // document are downstream consequences and are suppressed.
            ValidationIssue primaryIssue = issues.MinBy(issue => issue.Code, StringComparer.Ordinal)!;
            return new LandXmlParseResult(null, [primaryIssue]);
        }

        LandXmlSurfaceSummary summary = new(
            SupportedVersion,
            surfaceName!,
            pointCount,
            faceCount,
            epsgCode,
            horizontalUnit!.Value,
            verticalUnitFamily!.Value);
        return new LandXmlParseResult(summary, Array.Empty<ValidationIssue>());
    }

    private static void ParseUnits(
        XmlReader reader,
        ref LinearUnit? horizontalUnit,
        ref VerticalUnitFamily? verticalUnitFamily,
        ref bool envelopeInvalid)
    {
        LinearUnit? parsedHorizontalUnit = reader.GetAttribute("linearUnit") switch
        {
            "meter" => LinearUnit.Metre,
            "foot" => LinearUnit.InternationalFoot,
            "USSurveyFoot" => LinearUnit.UsSurveyFoot,
            _ => null
        };
        VerticalUnitFamily? parsedVerticalUnitFamily = reader.GetAttribute("elevationUnit") switch
        {
            "meter" => VerticalUnitFamily.Metre,
            "feet" => VerticalUnitFamily.Foot,
            _ => null
        };

        if (parsedHorizontalUnit is null || parsedVerticalUnitFamily is null)
        {
            envelopeInvalid = true;
            return;
        }

        horizontalUnit = parsedHorizontalUnit;
        verticalUnitFamily = parsedVerticalUnitFamily;
    }

    private static LeafContent ReadLeafContent(XmlReader reader)
    {
        int elementDepth = reader.Depth;
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return new LeafContent([], false, false, 0);
        }

        LeafTokenCollector tokens = new();
        char[] valueBuffer = new char[ValueChunkLength];
        bool hasChildElement = false;
        long sameNamespaceSurfaceCount = 0;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == elementDepth)
            {
                tokens.CompleteToken();
                reader.Read();
                return new LeafContent(
                    tokens.Tokens,
                    tokens.Invalid,
                    hasChildElement,
                    sameNamespaceSurfaceCount);
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                hasChildElement = true;
                if (reader.LocalName == "Surface" && reader.NamespaceURI == LandXmlNamespace)
                {
                    sameNamespaceSurfaceCount++;
                }
            }
            else if (reader.Depth == elementDepth + 1 &&
                reader.NodeType is XmlNodeType.Text or
                    XmlNodeType.CDATA or
                    XmlNodeType.Whitespace or
                    XmlNodeType.SignificantWhitespace)
            {
                ReadValueChunks(reader, valueBuffer, tokens);
            }
        }

        throw new XmlException("A LandXML point or face element is not closed.");
    }

    private static void ParsePoint(
        string? idText,
        LeafContent pointContent,
        IDictionary<long, Point3> points,
        ICollection<ValidationIssue> issues,
        ref long pointCount)
    {
        IReadOnlyList<string> tokens = pointContent.Tokens;
        if (!TryParsePositiveInt64(idText, out long pointId) ||
            pointContent.InvalidText ||
            tokens.Count != 3 ||
            !TryParseDouble(tokens[0], out double northing) ||
            !TryParseDouble(tokens[1], out double easting) ||
            !TryParseDouble(tokens[2], out double elevation))
        {
            AddIssueOnce(issues, IssueCodes.LandXmlInvalidPoint, "A TIN point is malformed.");
            return;
        }

        if (points.ContainsKey(pointId))
        {
            AddIssueOnce(issues, IssueCodes.LandXmlDuplicatePointId, "TIN point identifiers must be unique.");
            return;
        }

        if (!double.IsFinite(northing) || !double.IsFinite(easting) || !double.IsFinite(elevation))
        {
            AddIssueOnce(issues, IssueCodes.LandXmlNonfiniteCoordinate, "TIN point coordinates must be finite.");
            return;
        }

        points.Add(pointId, new Point3(northing, easting, elevation));
        pointCount++;
    }

    private static void ParseFace(
        LeafContent faceContent,
        IReadOnlyDictionary<long, Point3> points,
        ICollection<ValidationIssue> issues)
    {
        IReadOnlyList<string> tokens = faceContent.Tokens;
        if (faceContent.InvalidText ||
            tokens.Count != 3 ||
            !TryParsePositiveInt64(tokens[0], out long firstId) ||
            !TryParsePositiveInt64(tokens[1], out long secondId) ||
            !TryParsePositiveInt64(tokens[2], out long thirdId))
        {
            AddIssueOnce(issues, IssueCodes.LandXmlInvalidFace, "A TIN face is malformed.");
            return;
        }

        bool unknownReference =
            !points.TryGetValue(firstId, out Point3 first) |
            !points.TryGetValue(secondId, out Point3 second) |
            !points.TryGetValue(thirdId, out Point3 third);
        bool repeatedVertex = firstId == secondId || secondId == thirdId || thirdId == firstId;

        if (unknownReference)
        {
            AddIssueOnce(issues, IssueCodes.LandXmlUnknownPointReference, "A TIN face references an unknown point.");
        }

        if (repeatedVertex)
        {
            AddIssueOnce(issues, IssueCodes.LandXmlRepeatedFaceVertex, "A TIN face must reference three distinct points.");
        }

        if (unknownReference || repeatedVertex)
        {
            return;
        }

        double scale = GeometryScale(first, second, third);
        double secondEasting = ScaledDifference(second.Easting, first.Easting, scale);
        double secondNorthing = ScaledDifference(second.Northing, first.Northing, scale);
        double thirdEasting = ScaledDifference(third.Easting, first.Easting, scale);
        double thirdNorthing = ScaledDifference(third.Northing, first.Northing, scale);
        double cross = secondEasting * thirdNorthing - secondNorthing * thirdEasting;
        double maxSquaredEdge = Math.Max(
            ScaledSquaredDistance(first, second, scale),
            Math.Max(
                ScaledSquaredDistance(second, third, scale),
                ScaledSquaredDistance(third, first, scale)));
        bool degenerate = !double.IsFinite(cross) ||
            !double.IsFinite(maxSquaredEdge) ||
            maxSquaredEdge == 0d ||
            Math.Abs(cross) <= 1e-12 * maxSquaredEdge;
        if (degenerate)
        {
            AddIssueOnce(issues, IssueCodes.LandXmlDegenerateFace, "A TIN face has zero or near-zero horizontal area.");
        }
    }

    private static double GeometryScale(Point3 first, Point3 second, Point3 third)
    {
        double maximumCoordinate = Math.Max(
            Math.Max(Math.Abs(first.Northing), Math.Abs(first.Easting)),
            Math.Max(
                Math.Max(Math.Abs(second.Northing), Math.Abs(second.Easting)),
                Math.Max(Math.Abs(third.Northing), Math.Abs(third.Easting))));
        return maximumCoordinate == 0d
            ? 1d
            : Math.ScaleB(1d, Math.ILogB(maximumCoordinate));
    }

    private static double ScaledSquaredDistance(Point3 first, Point3 second, double scale)
    {
        double northing = ScaledDifference(second.Northing, first.Northing, scale);
        double easting = ScaledDifference(second.Easting, first.Easting, scale);
        return northing * northing + easting * easting;
    }

    private static double ScaledDifference(double left, double right, double scale) =>
        (left / scale) - (right / scale);

    private static void ReadValueChunks(
        XmlReader reader,
        char[] valueBuffer,
        LeafTokenCollector tokens)
    {
        if (!reader.CanReadValueChunk)
        {
            tokens.Append(reader.Value.AsSpan());
            return;
        }

        int countRead;
        while ((countRead = reader.ReadValueChunk(valueBuffer, 0, valueBuffer.Length)) > 0)
        {
            tokens.Append(valueBuffer.AsSpan(0, countRead));
        }
    }

    private static bool TryParseDouble(string? value, out double parsed) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);

    private static bool TryParsePositiveInt32(string? value, out int parsed) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0;

    private static bool TryParsePositiveInt64(string? value, out long parsed) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0;

    private static bool IsLandXmlElement(ElementFrame element, string localName) =>
        element.LocalName == localName && element.NamespaceUri == LandXmlNamespace;

    private static bool HasLandXmlAncestorPath(
        IReadOnlyList<ElementFrame> ancestors,
        params string[] localNames)
    {
        if (ancestors.Count != localNames.Length)
        {
            return false;
        }

        for (int index = 0; index < localNames.Length; index++)
        {
            if (!IsLandXmlElement(ancestors[index], localNames[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddIssueOnce(
        ICollection<ValidationIssue> issues,
        string code,
        string message)
    {
        if (!issues.Any(issue => issue.Code == code))
        {
            issues.Add(new ValidationIssue(code, IssueSeverity.Error, message));
        }
    }

    private static LandXmlParseResult Invalid(string code, string message) =>
        new(null, [new ValidationIssue(code, IssueSeverity.Error, message)]);

    private readonly record struct ElementFrame(string LocalName, string NamespaceUri);

    private readonly record struct LeafContent(
        IReadOnlyList<string> Tokens,
        bool InvalidText,
        bool HasChildElement,
        long SameNamespaceSurfaceCount);

    private sealed class LeafTokenCollector
    {
        private readonly List<string> tokens = [];
        private readonly StringBuilder currentToken = new(MaximumScalarTokenLength);
        private bool tokenStarted;

        internal IReadOnlyList<string> Tokens => tokens;

        internal bool Invalid { get; private set; }

        internal void Append(ReadOnlySpan<char> value)
        {
            foreach (char character in value)
            {
                if (char.IsWhiteSpace(character))
                {
                    CompleteToken();
                    continue;
                }

                tokenStarted = true;
                if (currentToken.Length < MaximumScalarTokenLength)
                {
                    currentToken.Append(character);
                }
                else
                {
                    Invalid = true;
                }
            }
        }

        internal void CompleteToken()
        {
            if (!tokenStarted)
            {
                return;
            }

            if (tokens.Count < MaximumLeafTokenCount)
            {
                tokens.Add(currentToken.ToString());
            }
            else
            {
                Invalid = true;
            }

            currentToken.Clear();
            tokenStarted = false;
        }
    }
}
