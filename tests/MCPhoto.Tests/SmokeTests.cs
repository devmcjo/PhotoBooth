using MCPhoto.Core.Models;

namespace MCPhoto.Tests;

/// <summary>솔루션 스캐폴드·프로젝트 참조 검증(WBS Step 1).</summary>
public class SmokeTests
{
    [Fact]
    public void Core_Models_Are_Referenceable()
    {
        var role = UserRoleExtensions.ParseRole("admin");
        Assert.Equal(UserRole.Admin, role);
        Assert.True(role.IsPower());
    }

    [Fact]
    public void Slot_AspectRatio_Computes()
    {
        var slot = new Slot { Width = 300, Height = 400 };
        Assert.Equal(0.75, slot.AspectRatio, 3);
    }
}
