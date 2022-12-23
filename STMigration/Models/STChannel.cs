// Copyright (c) Isak Viste. All rights reserved.
// Licensed under the MIT License.

namespace STMigration.Models;

#pragma warning disable IDE1006

public class STChannel {
    public string displayName { get; set; }
    public string description { get; set; }
    public string createdDateTime { get; set; }
    public string membershipType { get; set; } = "standard";

    public STChannel(string displayName, string description, string createdDateTime) {
        this.displayName = displayName;
        this.description = description;
        this.createdDateTime = createdDateTime;
    }

    public STChannel(string dirName, string createdDateTime) {
        displayName = dirName;
        description = $"Description for {dirName}";
        this.createdDateTime = createdDateTime;
    }
}