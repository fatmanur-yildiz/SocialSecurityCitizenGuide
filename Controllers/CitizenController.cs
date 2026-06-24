using System.Security.Claims;
using EmekliRehberi.Data;
using EmekliRehberi.Models;
using EmekliRehberi.Services;
using EmekliRehberi.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmekliRehberi.Controllers
{
    [Authorize(Roles = "Vatandas")]
    public class CitizenController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly PremiumPdfParser _premiumPdfParser;
        private readonly SocialSecurityRecordParser _socialSecurityParser;

        public CitizenController(
    AppDbContext context,
    IWebHostEnvironment environment,
    PremiumPdfParser premiumPdfParser,
    SocialSecurityRecordParser socialSecurityParser)
{
    _context = context;
    _environment = environment;
    _premiumPdfParser = premiumPdfParser;
    _socialSecurityParser = socialSecurityParser;
}

        public IActionResult Index()
        {
            return View();
        }

        // Eski Hizmet Dökümü sayfası şimdilik dursun, artık aktif kullanmıyoruz.
        [HttpGet]
        public async Task<IActionResult> ServiceStatement()
        {
            var userId = GetCurrentUserId();

            var records = await _context.ServiceStatements
                .Where(x => x.AppUserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            ViewBag.Records = records;

            return View(new ServiceStatementViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> ServiceStatement(ServiceStatementViewModel model)
        {
            var userId = GetCurrentUserId();

            if (!ModelState.IsValid)
            {
                ViewBag.Records = await _context.ServiceStatements
                    .Where(x => x.AppUserId == userId)
                    .OrderByDescending(x => x.CreatedAt)
                    .ToListAsync();

                return View(model);
            }

            string? fileName = null;

            if (model.PdfFile != null && model.PdfFile.Length > 0)
            {
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");

                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                fileName = Guid.NewGuid().ToString() + Path.GetExtension(model.PdfFile.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);

                using var stream = new FileStream(filePath, FileMode.Create);
                await model.PdfFile.CopyToAsync(stream);
            }

            var statement = new ServiceStatement
            {
                AppUserId = userId,
                UploadedFileName = fileName,
                OriginalFileName = model.PdfFile?.FileName,
                Status = "Yüklendi"
            };

            _context.ServiceStatements.Add(statement);
            await _context.SaveChangesAsync();

            return RedirectToAction("ServiceStatement");
        }

        // PRİM BİLGİLERİM - GET
        [HttpGet]
        public async Task<IActionResult> PremiumInfo()
        {
            var userId = GetCurrentUserId();

            var latestPremiumDocument = await _context.PremiumDocuments
    .Include(x => x.Records)
    .Where(x => x.AppUserId == userId)
    .OrderByDescending(x => x.CreatedAt)
    .FirstOrDefaultAsync();

            ViewBag.LatestPremiumDocument = latestPremiumDocument;

            return View(new PremiumUploadViewModel());
        }

        // PRİM BİLGİLERİM - PDF YÜKLEME
        [HttpPost]
        public async Task<IActionResult> PremiumInfo(PremiumUploadViewModel model)
        {
            var userId = GetCurrentUserId();

            var latestPremiumDocument = await _context.PremiumDocuments
                .Where(x => x.AppUserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (!ModelState.IsValid)
            {
                ViewBag.LatestPremiumDocument = latestPremiumDocument;
                return View(model);
            }

            if (model.PdfFile == null || model.PdfFile.Length == 0)
            {
                ModelState.AddModelError("PdfFile", "PDF dosyası seçilmelidir.");
                ViewBag.LatestPremiumDocument = latestPremiumDocument;
                return View(model);
            }

            var extension = Path.GetExtension(model.PdfFile.FileName).ToLower();

            if (extension != ".pdf")
            {
                ModelState.AddModelError("PdfFile", "Sadece PDF dosyası yükleyebilirsiniz.");
                ViewBag.LatestPremiumDocument = latestPremiumDocument;
                return View(model);
            }

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "premium-documents");

            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var uploadedFileName = Guid.NewGuid().ToString() + extension;
            var filePath = Path.Combine(uploadsFolder, uploadedFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await model.PdfFile.CopyToAsync(stream);
            }

            var analysis = _premiumPdfParser.AnalyzePdf(filePath);

var premiumDocument = new PremiumDocument
{
    AppUserId = userId,
    OriginalFileName = model.PdfFile.FileName,
    UploadedFileName = uploadedFileName,
    DocumentType = "SGK Tescil ve Hizmet Dökümü",
    Status = "Analiz edildi",
    ExtractedText = analysis.ExtractedText,
    TotalPremiumDays = analysis.TotalPremiumDays,
    LastPeriod = analysis.LastPeriod,
    HasMissingDays = analysis.HasMissingDays
};


foreach (var item in analysis.Records)
{
    premiumDocument.Records.Add(new PremiumRecord
    {
        Period     = item.Period,
        Days       = item.Days,
        PekAmount  = item.PekAmount,
        IsYearTotal = item.IsYearTotal,
        EntryDate  = item.EntryDate,
        ExitDate   = item.ExitDate
    });
}
_context.PremiumDocuments.Add(premiumDocument);
await _context.SaveChangesAsync();

            return RedirectToAction("PremiumInfo");
        }

       [HttpGet]
public async Task<IActionResult> RetirementStatus()
{
    var userId = GetCurrentUserId();

    var latestPremium = await _context.PremiumDocuments
        .Include(x => x.Records)
        .Where(x => x.AppUserId == userId)
        .OrderByDescending(x => x.CreatedAt)
        .FirstOrDefaultAsync();

    var latestSocialSecurity = await _context.SocialSecurityRecordDocuments
        .Where(x => x.AppUserId == userId)
        .OrderByDescending(x => x.CreatedAt)
        .FirstOrDefaultAsync();

    ViewBag.LatestPremium = latestPremium;
    ViewBag.LatestSocialSecurity = latestSocialSecurity;

    // İlk sigorta başlangıcı: tek ve tutarlı kaynak — InsuranceDateResolver.
    // GET aşamasında henüz kullanıcıdan manuel bir giriş olmadığı için
    // manualDate = null veriliyor; form alanı da bilerek BOŞ bırakılıyor
    // (sistemin tahminiyle önceden doldurulmuyor) — aksi halde formu hiç
    // değiştirmeden gönderen kullanıcı, sistemin kendi tahminini "manuel giriş"
    // olarak geri göndermiş gibi görünür ve öncelik sırası bozulurdu.
    ViewBag.ResolvedInsuranceDate = InsuranceDateResolver.Resolve(null, latestPremium, latestSocialSecurity);

    var model = new RetirementInputViewModel();

    return View(model);
}

[HttpPost]
public async Task<IActionResult> RetirementStatus(RetirementInputViewModel model)
{
    var userId = GetCurrentUserId();

    if (model.SocialSecurityPdf != null && model.SocialSecurityPdf.Length > 0)
    {
        var ext = Path.GetExtension(model.SocialSecurityPdf.FileName).ToLower();

        if (ext != ".pdf")
        {
            ModelState.AddModelError("SocialSecurityPdf", "Sadece PDF dosyası yükleyebilirsiniz.");
        }
        else
        {
            var folder = Path.Combine(_environment.WebRootPath, "uploads", "social-security");

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var uploadedName = Guid.NewGuid().ToString() + ext;
            var filePath = Path.Combine(folder, uploadedName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await model.SocialSecurityPdf.CopyToAsync(stream);
            }

            var parsed = _socialSecurityParser.Parse(filePath);

            parsed.AppUserId = userId;
            parsed.OriginalFileName = model.SocialSecurityPdf.FileName;
            parsed.UploadedFileName = uploadedName;

            _context.SocialSecurityRecordDocuments.Add(parsed);
            await _context.SaveChangesAsync();
        }
    }

    var latestPremium = await _context.PremiumDocuments
        .Include(x => x.Records)
        .Where(x => x.AppUserId == userId)
        .OrderByDescending(x => x.CreatedAt)
        .FirstOrDefaultAsync();

    var latestSocialSecurity = await _context.SocialSecurityRecordDocuments
        .Where(x => x.AppUserId == userId)
        .OrderByDescending(x => x.CreatedAt)
        .FirstOrDefaultAsync();

    ViewBag.LatestPremium = latestPremium;
    ViewBag.LatestSocialSecurity = latestSocialSecurity;
    ViewBag.ManualInput = model;

    // model.FirstInsuranceDate artık SADECE kullanıcının formda gerçekten
    // yazdığı tarihi temsil ediyor (GET'te boş bırakıldığı için).
    ViewBag.ResolvedInsuranceDate = InsuranceDateResolver.Resolve(model.FirstInsuranceDate, latestPremium, latestSocialSecurity);

    return View(model);
}

        public async Task<IActionResult> DebtOptions()
        {
            var userId = GetCurrentUserId();

            var latestStatement = await _context.ServiceStatements
                .Where(x => x.AppUserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            ViewBag.LatestStatement = latestStatement;

            return View();
        }

        // HİZMET BİRLEŞTİRME - GET
        [HttpGet]
        public async Task<IActionResult> ServiceMerge()
        {
            var userId = GetCurrentUserId();

            var latestPremium = await _context.PremiumDocuments
                .Include(x => x.Records)
                .Where(x => x.AppUserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            var latestSocialSecurity = await _context.SocialSecurityRecordDocuments
                .Where(x => x.AppUserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            var result = ServiceMergeAnalysisService.Analyze(latestPremium, latestSocialSecurity, null);

            ViewBag.LatestPremium = latestPremium;
            ViewBag.LatestSocialSecurity = latestSocialSecurity;
            ViewBag.Result = result;

            return View(new ServiceMergeInputViewModel());
        }

        // HİZMET BİRLEŞTİRME - HEDEF STATÜ SEÇİMİ
        [HttpPost]
        public async Task<IActionResult> ServiceMerge(ServiceMergeInputViewModel model)
        {
            var userId = GetCurrentUserId();

            var latestPremium = await _context.PremiumDocuments
                .Include(x => x.Records)
                .Where(x => x.AppUserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            var latestSocialSecurity = await _context.SocialSecurityRecordDocuments
                .Where(x => x.AppUserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            var result = ServiceMergeAnalysisService.Analyze(latestPremium, latestSocialSecurity, model.TargetStatus);

            ViewBag.LatestPremium = latestPremium;
            ViewBag.LatestSocialSecurity = latestSocialSecurity;
            ViewBag.Result = result;

            return View(model);
        }

        public IActionResult ApplicationGuide()
        {
            return View();
        }

        // SIK SORULAN SORULAR — tüm sistem için tek, ortak sayfa; giriş zorunlu değil.
        [AllowAnonymous]
        public IActionResult Faq()
        {
            return View();
        }

        public async Task<IActionResult> RecentActions()
        {
            var userId = GetCurrentUserId();

            var premiumDocuments = await _context.PremiumDocuments
                .Where(x => x.AppUserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            ViewBag.PremiumDocuments = premiumDocuments;

            return View();
        }

        public async Task<IActionResult> Profile()
        {
            var userId = GetCurrentUserId();

            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.Id == userId);

            ViewBag.CurrentUser = user;

            return View();
        }

        private int GetCurrentUserId()
        {
            return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        }
    }
}