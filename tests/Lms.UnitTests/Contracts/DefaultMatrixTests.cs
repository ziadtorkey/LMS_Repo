using Lms.Shared.Authorization;
using Lms.Shared.Messaging;

namespace Lms.UnitTests.Contracts;

/// <summary>The D69 default matrix, cell by cell, against the written table (the oracle): a change to a default must change this table on purpose (QA2, L4).</summary>
public sealed class DefaultMatrixTests
{
    private const string D = "Deny", R = "Relationship", S = "SameUnit";

    private static readonly string[] Order =
        [BuiltInRoles.Student, BuiltInRoles.Guardian, BuiltInRoles.Teacher, BuiltInRoles.Counselor, BuiltInRoles.Supervisor, BuiltInRoles.Manager, BuiltInRoles.OrgAdmin];

    // Rows are the sender, columns the recipient, in the order above.
    private static readonly string[][] Table =
    [
        /* Student    */ [D, D, R, R, D, D, D],
        /* Guardian   */ [D, D, R, R, D, D, D],
        /* Teacher    */ [R, R, S, S, S, S, S],
        /* Counselor  */ [R, R, S, S, S, S, S],
        /* Supervisor */ [R, R, S, S, S, S, S],
        /* Manager    */ [D, S, S, S, S, S, S],
        /* Org Admin  */ [D, S, S, S, S, S, S],
    ];

    [Fact]
    public void Every_cell_of_the_default_matrix_equals_the_written_table()
    {
        for (var from = 0; from < Order.Length; from++)
        {
            for (var to = 0; to < Order.Length; to++)
            {
                Assert.True(Table[from][to] == MessagingRuleDefaults.Default(Order[from], Order[to]), $"{Order[from]}>{Order[to]}: expected {Table[from][to]}, was {MessagingRuleDefaults.Default(Order[from], Order[to])}");
            }
        }
    }

    [Fact]
    public void No_default_gives_the_whole_client_to_anyone_and_no_staff_reaches_a_student_without_a_relationship()
    {
        Assert.DoesNotContain(MessagingRuleDefaults.Matrix(), c => c.Mode == MessagingRuleModes.Allow);
        Assert.All(new[] { BuiltInRoles.Manager, BuiltInRoles.OrgAdmin }, role => Assert.Equal(D, MessagingRuleDefaults.Default(role, BuiltInRoles.Student)));
        Assert.Equal(Order.Length * Order.Length, MessagingRuleDefaults.Matrix().Count);
    }
}
