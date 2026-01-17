using Microsoft.AspNetCore.Mvc;
using NetworkMonitor.Server.Services;
using NetworkMonitor.Shared.Models;
using System.Runtime.InteropServices;

namespace NetworkMonitor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MonitoringController : ControllerBase
{
    private readonly DeviceMonitorService _monitorService;

    public MonitoringController(DeviceMonitorService monitorService)
    {
        _monitorService = monitorService;
    }

    [HttpGet("devices")]
    public IActionResult GetDevices()
    {
        var devices = _monitorService.GetAllDeviceStates();
        return Ok(devices);
    }

    [HttpGet("statistics")]
    public IActionResult GetStatistics()
    {
        var devices = _monitorService.GetAllDeviceStates();

        var stats = new
        {
            TotalDevices = devices.Count,
            OnlineDevices = devices.Count(d => d.Status == "Online"),
            OfflineDevices = devices.Count(d => d.Status == "Offline"),
            StaleDevices = devices.Count(d => d.Status == "Stale"),
            AverageLatency = devices
                .Where(d => d.AverageLatency > 0)
                .Select(d => d.AverageLatency)
                .DefaultIfEmpty(0)
                .Average()
        };

        return Ok(stats);
    }

    [HttpGet("device/{ip}")]
    public IActionResult GetDevice(string ip)
    {
        var device = _monitorService.GetDeviceState(ip);

        if (device == null)
        {
            return NotFound();
        }

        return Ok(device);
    }
}