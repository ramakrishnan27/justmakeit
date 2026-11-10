namespace Platform.Content.Enums;

public enum ContentVisibility
{
    [DbValue("public")] Public,
    [DbValue("private")] Private,
    [DbValue("unlisted")] Unlisted
}
