// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.InMemory.Internal;
using Microsoft.EntityFrameworkCore.TestModels.GearsOfWarModel;
using Xunit.Sdk;

namespace Microsoft.EntityFrameworkCore.Query;

public class GearsOfWarQueryInMemoryTest : GearsOfWarQueryTestBase<GearsOfWarQueryInMemoryFixture>
{
    public GearsOfWarQueryInMemoryTest(GearsOfWarQueryInMemoryFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        //TestLoggerFactory.TestOutputHelper = testOutputHelper;
    }

    public override Task Client_member_and_unsupported_string_Equals_in_the_same_query(bool async)
        => AssertTranslationFailedWithDetails(
            () => base.Client_member_and_unsupported_string_Equals_in_the_same_query(async),
            CoreStrings.QueryUnableToTranslateMember(nameof(Gear.IsMarcus), nameof(Gear)));

    public override async Task
        Null_semantics_is_correctly_applied_for_function_comparisons_that_take_arguments_from_optional_navigation_complex(bool async)
        => Assert.Equal(
            "Nullable object must have a value.",
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => base
                    .Null_semantics_is_correctly_applied_for_function_comparisons_that_take_arguments_from_optional_navigation_complex(
                        async))).Message);

    public override async Task Group_by_on_StartsWith_with_null_parameter_as_argument(bool async)
        // Grouping by constant. Issue #19683.
        => Assert.Equal(
            "1",
            (await Assert.ThrowsAsync<EqualException>(
                () => base.Group_by_on_StartsWith_with_null_parameter_as_argument(async)))
            .Actual);

    public override async Task Projecting_entity_as_well_as_correlated_collection_followed_by_Distinct(bool async)
        // Distinct. Issue #24325.
        => Assert.Equal(
            InMemoryStrings.DistinctOnSubqueryNotSupported,
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => base.Projecting_entity_as_well_as_correlated_collection_followed_by_Distinct(async))).Message);

    public override async Task Projecting_entity_as_well_as_complex_correlated_collection_followed_by_Distinct(bool async)
        // Distinct. Issue #24325.
        => Assert.Equal(
            InMemoryStrings.DistinctOnSubqueryNotSupported,
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => base.Projecting_entity_as_well_as_complex_correlated_collection_followed_by_Distinct(async))).Message);

    public override async Task Projecting_entity_as_well_as_correlated_collection_of_scalars_followed_by_Distinct(bool async)
        // Distinct. Issue #24325.
        => Assert.Equal(
            InMemoryStrings.DistinctOnSubqueryNotSupported,
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => base.Projecting_entity_as_well_as_correlated_collection_of_scalars_followed_by_Distinct(async))).Message);

    public override async Task Correlated_collection_with_distinct_3_levels(bool async)
        // Distinct. Issue #24325.
        => Assert.Equal(
            InMemoryStrings.DistinctOnSubqueryNotSupported,
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => base.Correlated_collection_with_distinct_3_levels(async))).Message);

    public override async Task Projecting_correlated_collection_followed_by_Distinct(bool async)
        // Distinct. Issue #24325.
        => Assert.Equal(
            InMemoryStrings.DistinctOnSubqueryNotSupported,
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => base.Projecting_correlated_collection_followed_by_Distinct(async))).Message);

    public override async Task Projecting_some_properties_as_well_as_correlated_collection_followed_by_Distinct(bool async)
        // Distinct. Issue #24325.
        => Assert.Equal(
            InMemoryStrings.DistinctOnSubqueryNotSupported,
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => base.Projecting_some_properties_as_well_as_correlated_collection_followed_by_Distinct(async))).Message);

    public override Task Include_after_SelectMany_throws(bool async)
        => Assert.ThrowsAsync<NullReferenceException>(() => base.Include_after_SelectMany_throws(async));

    public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result4(bool async)
        => Assert.Equal(
            "4",
            (((EqualException)(await Assert.ThrowsAsync<TargetInvocationException>(
                () => base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result4(async))).InnerException!.InnerException)!)
            .Actual);

    public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_complex_projection_result(bool async)
        => Assert.Equal(
            "6",
            (((EqualException)(await Assert.ThrowsAsync<TargetInvocationException>(
                    () => base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_complex_projection_result(async)))
                .InnerException!.InnerException)!)
            .Actual);

    public override Task Null_semantics_is_correctly_applied_for_function_comparisons_that_take_arguments_from_optional_navigation(
        bool async)
        // Null protection. Issue #13721.
        => Assert.ThrowsAsync<InvalidOperationException>(
            () => base.Null_semantics_is_correctly_applied_for_function_comparisons_that_take_arguments_from_optional_navigation(async));
}
