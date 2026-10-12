using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Auka.Application.Entities;
using Auka.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auka.Web.Controllers;

[Authorize]
public class AsistenciaController : Controller
{
    private readonly ApplicationDbContext _context;

    public AsistenciaController(ApplicationDbContext context)
    {
        _context = context;
    }

    // 📌 NIVEL 2: Resumen por cursos con porcentaje general
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var colegioClaim = User.FindFirst("ColegioId")?.Value;
        int.TryParse(colegioClaim, out int colegioId);
        colegioId = colegioId > 0 ? colegioId : 1;

        // Cursos del colegio con su cantidad de estudiantes
        var cursos = await _context.Cursos
            .Where(c => c.ColegioId == colegioId)
            .Select(c => new
            {
                c.Id,
                c.Nombre,
                c.Letra,
                TotalEstudiantes = c.Estudiantes.Count
            })
            .OrderBy(c => c.Nombre)
            .ThenBy(c => c.Letra)
            .ToListAsync();

        // Asistencia agrupada por curso, obtenida a través del estudiante
        // (Asistencia no tiene CursoId; el curso vive en Estudiante)
        var asistenciaPorCurso = await (
            from a in _context.Asistencias
            join e in _context.Estudiantes on a.EstudianteId equals e.Id
            group a by e.CursoId into g
            select new
            {
                CursoId = g.Key,
                Total = g.Count(),
                Presentes = g.Count(x => x.Presente)
            })
            .ToListAsync();

        var resumenCursos = cursos.Select(c =>
        {
            var datos = asistenciaPorCurso.FirstOrDefault(x => x.CursoId == c.Id);
            int total = datos?.Total ?? 0;
            int presentes = datos?.Presentes ?? 0;

            return new
            {
                c.Id,
                CursoNombre = $"{c.Nombre} {c.Letra}",
                c.TotalEstudiantes,
                PorcentajeAsistencia = total > 0
                    ? Math.Round((double)presentes / total * 100, 1)
                    : 0.0
            };
        }).ToList();

        ViewBag.CursosResumen = resumenCursos;
        return View();
    }

    // 📌 NIVEL 3: Detalle por alumno del curso
    [HttpGet]
    public async Task<IActionResult> Curso(int id)
    {
        var curso = await _context.Cursos.FirstOrDefaultAsync(c => c.Id == id);
        if (curso == null) return NotFound();

        var estudiantes = await _context.Estudiantes
            .Include(e => e.Usuario)
            .Where(e => e.CursoId == id)
            .ToListAsync();

        var detalleEstudiantes = new List<object>();

        foreach (var est in estudiantes)
        {
            var historialAsistencia = await _context.Asistencias
                .Where(a => a.EstudianteId == est.Id)
                .OrderByDescending(a => a.Fecha)
                .Take(20)
                .Select(a => new { a.Fecha, a.Presente })
                .ToListAsync();

            int totalDias = historialAsistencia.Count;
            int diasPresente = historialAsistencia.Count(a => a.Presente);
            double porcentajeAlumno = totalDias > 0
                ? Math.Round((double)diasPresente / totalDias * 100, 1)
                : 0.0;

            detalleEstudiantes.Add(new
            {
                est.Id,
                NombreCompleto = est.Usuario != null
                    ? $"{est.Usuario.Nombre} {est.Usuario.Apellido}"
                    : "Estudiante sin nombre",
                Rut = est.Usuario?.Rut ?? "S/R",
                Porcentaje = porcentajeAlumno,
                DiasPresentes = diasPresente,
                DiasAusentes = totalDias - diasPresente,
                Historial = historialAsistencia
            });
        }

        ViewBag.CursoId = curso.Id;
        ViewBag.CursoNombre = $"{curso.Nombre} {curso.Letra}";
        ViewBag.EstudiantesDetalle = detalleEstudiantes;

        return View();
    }
}