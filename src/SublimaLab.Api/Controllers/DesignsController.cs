using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SublimaLab.Application.Customization;

namespace SublimaLab.Api.Controllers;

[ApiController]
[Route("api/designs")]
public sealed class DesignsController(DesignService designs) : ControllerBase
{
    /// <summary>
    /// Validates a design against a product without saving it. Available to guests so the
    /// customizer can show placement errors and print-quality warnings while editing.
    /// </summary>
    [HttpPost("validate")]
    public async Task<ActionResult<DesignValidationResult>> Validate(ValidateDesignRequest request, CancellationToken cancellationToken) =>
        Ok(await designs.ValidateAsync(request, cancellationToken));

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<DesignDto>>> List(CancellationToken cancellationToken) =>
        Ok(await designs.ListMineAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<DesignDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await designs.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<DesignDto>> Create(SaveDesignRequest request, CancellationToken cancellationToken)
    {
        var design = await designs.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = design.Id }, design);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<DesignDto>> Update(Guid id, SaveDesignRequest request, CancellationToken cancellationToken) =>
        Ok(await designs.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await designs.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
