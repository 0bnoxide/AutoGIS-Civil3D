namespace AutoGIS.Civil3D.Handoff.Tests;

internal static class TestManifests
{
    internal const string KnownDatum =
        """
        {"contract_version":"1.0","package_id":"9a8ff271-b0d8-46db-809d-a6f72954af20","created_utc":"2026-08-02T00:00:00Z","producer":{"name":"AutoGIS","version":"1.0.0","source_commit":"0123456789abcdef"},"surface":{"filename":"surface.landxml","sha256":"eecb977d69ff86eec34d02d881991edd5533eee77e8b854e68cbfcab69ea0af9","landxml_version":"1.2","name":"Existing Ground","point_count":4,"face_count":2},"coordinate_reference":{"horizontal":{"kind":"projected","authority":"EPSG","code":2256,"unit":"us_survey_foot"},"vertical":{"unit":"us_survey_foot","direction":"positive_up","datum":{"status":"known","authority":"EPSG","code":5703,"name":"NAVD88 height"}}}}
        """;

    internal const string UnknownDatum =
        """
        {"contract_version":"1.0","package_id":"9a8ff271-b0d8-46db-809d-a6f72954af20","created_utc":"2026-08-02T00:00:00Z","producer":{"name":"AutoGIS","version":"1.0.0","source_commit":"0123456789abcdef"},"surface":{"filename":"surface.landxml","sha256":"eecb977d69ff86eec34d02d881991edd5533eee77e8b854e68cbfcab69ea0af9","landxml_version":"1.2","name":"Existing Ground","point_count":4,"face_count":2},"coordinate_reference":{"horizontal":{"kind":"projected","authority":"EPSG","code":2256,"unit":"us_survey_foot"},"vertical":{"unit":"us_survey_foot","direction":"positive_up","datum":{"status":"unknown","note":"Confirm project datum before import"}}}}
        """;

    internal static string WithCreatedUtc(string value) => KnownDatum.Replace(
        "\"created_utc\":\"2026-08-02T00:00:00Z\"",
        $"\"created_utc\":\"{value}\"",
        StringComparison.Ordinal);
}
