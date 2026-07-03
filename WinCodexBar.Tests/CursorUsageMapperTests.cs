using WinCodexBar.Core.Services;

namespace WinCodexBar.Tests;

public class CursorUsageMapperTests
{
    private const string CycleStart = "2026-06-15T00:00:00Z";
    private const string CycleEnd = "2026-07-15T00:00:00Z";
    private static readonly DateTimeOffset CycleEndDate = DateTimeOffset.Parse(CycleEnd);
    private const int CycleMinutes = 30 * 24 * 60;

    [Fact]
    public void Map_TotalPercentUsedPresent_UsesItForPrimaryAndAutoForSecondary()
    {
        var summary = SummaryWithPlan(new CursorPlanUsage
        {
            TotalPercentUsed = 42.5,
            AutoPercentUsed = 10.25,
            ApiPercentUsed = 74.75
        });

        var mapped = CursorUsageMapper.Map(summary);

        Assert.NotNull(mapped.Primary);
        Assert.Equal("Total", mapped.Primary!.Label);
        Assert.Equal(42.5, mapped.Primary.UsedPercent);
        Assert.Equal(CycleMinutes, mapped.Primary.WindowMinutes);
        Assert.Equal(CycleEndDate, mapped.Primary.ResetsAt);

        Assert.NotNull(mapped.Secondary);
        Assert.Equal("Auto", mapped.Secondary!.Label);
        Assert.Equal(10.25, mapped.Secondary.UsedPercent);
        Assert.Equal(CycleMinutes, mapped.Secondary.WindowMinutes);
        Assert.Equal(CycleEndDate, mapped.Secondary.ResetsAt);
    }

    [Fact]
    public void Map_TotalPercentMissing_AveragesAutoAndApiPercents()
    {
        var summary = SummaryWithPlan(new CursorPlanUsage
        {
            AutoPercentUsed = 20,
            ApiPercentUsed = 40
        });

        var mapped = CursorUsageMapper.Map(summary);

        Assert.Equal(30, mapped.Primary!.UsedPercent);
    }

    [Fact]
    public void Map_OnlyApiPercentPresent_UsesApiLaneAndOmitsSecondary()
    {
        var summary = SummaryWithPlan(new CursorPlanUsage { ApiPercentUsed = 40 });

        var mapped = CursorUsageMapper.Map(summary);

        Assert.Equal(40, mapped.Primary!.UsedPercent);
        Assert.Null(mapped.Secondary);
    }

    [Fact]
    public void Map_OnlyAutoPercentPresent_UsesAutoLane()
    {
        var summary = SummaryWithPlan(new CursorPlanUsage { AutoPercentUsed = 20 });

        var mapped = CursorUsageMapper.Map(summary);

        Assert.Equal(20, mapped.Primary!.UsedPercent);
        Assert.Equal(20, mapped.Secondary!.UsedPercent);
    }

    [Fact]
    public void Map_PercentLanesMissing_UsesPlanUsedLimitRatio()
    {
        var summary = SummaryWithPlan(new CursorPlanUsage { Used = 500, Limit = 2000 });

        var mapped = CursorUsageMapper.Map(summary);

        Assert.Equal(25, mapped.Primary!.UsedPercent);
        Assert.Null(mapped.Secondary);
    }

    [Fact]
    public void Map_PlanMissing_UsesOverallRatio()
    {
        var summary = new CursorUsageSummary
        {
            BillingCycleStart = CycleStart,
            BillingCycleEnd = CycleEnd,
            IndividualUsage = new CursorIndividualUsage
            {
                Overall = new CursorMoneyUsage { Used = 7384, Limit = 10000 }
            }
        };

        var mapped = CursorUsageMapper.Map(summary);

        Assert.NotNull(mapped.Primary!.UsedPercent);
        Assert.Equal(73.84, mapped.Primary.UsedPercent.Value, precision: 10);
    }

    [Fact]
    public void Map_IndividualUsageMissing_UsesPooledRatio()
    {
        var summary = new CursorUsageSummary
        {
            BillingCycleStart = CycleStart,
            BillingCycleEnd = CycleEnd,
            TeamUsage = new CursorTeamUsage
            {
                Pooled = new CursorMoneyUsage { Used = 2500, Limit = 10000 }
            }
        };

        var mapped = CursorUsageMapper.Map(summary);

        Assert.Equal(25, mapped.Primary!.UsedPercent);
    }

    [Fact]
    public void Map_NoUsageData_DefaultsPrimaryToZero()
    {
        var mapped = CursorUsageMapper.Map(new CursorUsageSummary());

        Assert.Equal(0, mapped.Primary!.UsedPercent);
        Assert.Null(mapped.Secondary);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-10, 0)]
    [InlineData(0.36, 0.36)]
    public void Map_OutOfRangeOrFractionalTotalPercent_IsClampedNotRescaled(double totalPercent, double expected)
    {
        var summary = SummaryWithPlan(new CursorPlanUsage { TotalPercentUsed = totalPercent });

        var mapped = CursorUsageMapper.Map(summary);

        Assert.Equal(expected, mapped.Primary!.UsedPercent);
    }

    [Fact]
    public void Map_OutOfRangeAutoPercent_IsClamped()
    {
        var summary = SummaryWithPlan(new CursorPlanUsage
        {
            TotalPercentUsed = 50,
            AutoPercentUsed = 120.5
        });

        var mapped = CursorUsageMapper.Map(summary);

        Assert.Equal(100, mapped.Secondary!.UsedPercent);
    }

    [Fact]
    public void Map_MissingBillingDates_ProducesNullWindow()
    {
        var summary = new CursorUsageSummary
        {
            IndividualUsage = new CursorIndividualUsage
            {
                Plan = new CursorPlanUsage { TotalPercentUsed = 42, AutoPercentUsed = 12 }
            }
        };

        var mapped = CursorUsageMapper.Map(summary);

        Assert.Equal(42, mapped.Primary!.UsedPercent);
        Assert.Null(mapped.Primary.WindowMinutes);
        Assert.Null(mapped.Primary.ResetsAt);
        Assert.Null(mapped.Primary.ResetDescription);
        Assert.Null(mapped.Secondary!.WindowMinutes);
        Assert.Null(mapped.Secondary.ResetsAt);
    }

    [Fact]
    public void Map_MissingBillingStart_StillSetsResetsAtWithoutWindowMinutes()
    {
        var summary = SummaryWithPlan(new CursorPlanUsage { TotalPercentUsed = 42 });
        summary.BillingCycleStart = null;

        var mapped = CursorUsageMapper.Map(summary);

        Assert.Null(mapped.Primary!.WindowMinutes);
        Assert.Equal(CycleEndDate, mapped.Primary.ResetsAt);
    }

    [Fact]
    public void Map_LegacyRequestQuota_ProjectsRequestRatioAndHidesSecondary()
    {
        var summary = SummaryWithPlan(new CursorPlanUsage
        {
            TotalPercentUsed = 90,
            AutoPercentUsed = 50
        });

        var mapped = CursorUsageMapper.Map(summary, new CursorRequestQuota(Used: 125, Limit: 500));

        Assert.Equal("Total", mapped.Primary!.Label);
        Assert.Equal(25, mapped.Primary.UsedPercent);
        Assert.Equal(CycleMinutes, mapped.Primary.WindowMinutes);
        Assert.Equal(CycleEndDate, mapped.Primary.ResetsAt);
        Assert.Null(mapped.Secondary);
    }

    [Fact]
    public void Map_LegacyRequestQuotaOverLimit_IsClamped()
    {
        var summary = SummaryWithPlan(new CursorPlanUsage());

        var mapped = CursorUsageMapper.Map(summary, new CursorRequestQuota(Used: 600, Limit: 500));

        Assert.Equal(100, mapped.Primary!.UsedPercent);
    }

    [Fact]
    public void Map_RequestQuotaWithZeroLimit_FallsBackToPercentMapping()
    {
        var summary = SummaryWithPlan(new CursorPlanUsage
        {
            TotalPercentUsed = 42,
            AutoPercentUsed = 12
        });

        var mapped = CursorUsageMapper.Map(summary, new CursorRequestQuota(Used: 10, Limit: 0));

        Assert.Equal(42, mapped.Primary!.UsedPercent);
        Assert.Equal(12, mapped.Secondary!.UsedPercent);
    }

    [Fact]
    public void Map_DeserializedApiPayload_ProducesExpectedWindows()
    {
        const string json = """
        {
            "billingCycleStart": "2025-01-01T00:00:00.000Z",
            "billingCycleEnd": "2025-02-01T00:00:00.000Z",
            "membershipType": "pro",
            "individualUsage": {
                "plan": {
                    "enabled": true,
                    "used": 1500,
                    "limit": 5000,
                    "remaining": 3500,
                    "totalPercentUsed": 30.0,
                    "autoPercentUsed": 12.5
                },
                "onDemand": {
                    "enabled": true,
                    "used": 500,
                    "limit": 10000,
                    "remaining": 9500
                }
            },
            "teamUsage": {
                "onDemand": {
                    "enabled": true,
                    "used": 2000,
                    "limit": 50000,
                    "remaining": 48000
                }
            }
        }
        """;

        var summary = System.Text.Json.JsonSerializer.Deserialize<CursorUsageSummary>(json);

        Assert.NotNull(summary);
        var mapped = CursorUsageMapper.Map(summary!);

        Assert.Equal(30.0, mapped.Primary!.UsedPercent);
        Assert.Equal(12.5, mapped.Secondary!.UsedPercent);
        Assert.Equal(31 * 24 * 60, mapped.Primary.WindowMinutes);
        Assert.Equal(DateTimeOffset.Parse("2025-02-01T00:00:00Z"), mapped.Primary.ResetsAt);
    }

    private static CursorUsageSummary SummaryWithPlan(CursorPlanUsage plan)
    {
        return new CursorUsageSummary
        {
            BillingCycleStart = CycleStart,
            BillingCycleEnd = CycleEnd,
            IndividualUsage = new CursorIndividualUsage { Plan = plan }
        };
    }
}
