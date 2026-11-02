using Moq;
using PlateformeRHCollaborative.Web.Models;
using PlateformeRHCollaborative.Web.Repositories;
using PlateformeRHCollaborative.Web.Services;

namespace PlateformeRHCollaborative.Tests;

/// <summary>
/// Tests unitaires des règles métier des congés (LeaveService).
/// Les repositories sont simulés avec Moq : aucune base de données n'est utilisée.
/// </summary>
public class LeaveServiceTests
{
    // Les "doublures" des repositories (comme @Mock avec Mockito)
    private readonly Mock<ILeaveRepository> _leaveRepo = new();
    private readonly Mock<ITeleworkRepository> _teleworkRepo = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();

    // Le vrai service qu'on teste (comme @InjectMocks)
    private readonly LeaveService _service;

    public LeaveServiceTests()
    {
        // xUnit crée une nouvelle instance de cette classe pour CHAQUE test :
        // chaque test démarre donc avec des mocks tout neufs.
        _service = new LeaveService(_leaveRepo.Object, _teleworkRepo.Object, _employeeRepo.Object);
    }

    /// <summary>Un lundi dans environ 2 semaines : toujours plus de 48h dans le futur.</summary>
    private static DateTime ProchainLundi()
    {
        var date = DateTime.Today.AddDays(14);
        while (date.DayOfWeek != DayOfWeek.Monday)
            date = date.AddDays(1);
        return date;
    }

    // ---------------------------------------------------------------
    // 1. Calcul des jours ouvrables
    // ---------------------------------------------------------------
    [Fact]
    public void GetBusinessDays_LundiAuDimanche_Retourne5()
    {
        // Arrange
        var lundi = ProchainLundi();
        var dimanche = lundi.AddDays(6);

        // Act
        var jours = _service.GetBusinessDays(lundi, dimanche);

        // Assert : samedi et dimanche ne comptent pas
        Assert.Equal(5, jours);
    }

    // ---------------------------------------------------------------
    // 2. Règle des 48h
    // ---------------------------------------------------------------
    [Fact]
    public async Task AddAsync_MoinsDe48h_Refuse()
    {
        // Arrange : une demande qui commence dans 24h seulement
        var demande = new Leave
        {
            EmployeeId = 1,
            StartDate = DateTime.Now.AddHours(24),
            EndDate = DateTime.Now.AddDays(3)
        };

        // Act + Assert
        var erreur = await Assert.ThrowsAsync<BusinessException>(() => _service.AddAsync(demande));
        Assert.Contains("48h", erreur.Message);
        _leaveRepo.Verify(r => r.AddAsync(It.IsAny<Leave>()), Times.Never);
    }

    // ---------------------------------------------------------------
    // 3. Solde insuffisant
    // ---------------------------------------------------------------
    [Fact]
    public async Task AddAsync_SoldeInsuffisant_Refuse()
    {
        // Arrange : l'employé n'a que 2 jours, il en demande 5 (lundi -> vendredi)
        var lundi = ProchainLundi();
        _employeeRepo.Setup(r => r.GetByIdAsync(1))
                     .ReturnsAsync(new Employee { Id = 1, SoldeConges = 2, Role = "Employe" });
        var demande = new Leave { EmployeeId = 1, StartDate = lundi, EndDate = lundi.AddDays(4) };

        // Act + Assert
        var erreur = await Assert.ThrowsAsync<BusinessException>(() => _service.AddAsync(demande));
        Assert.Contains("Solde insuffisant", erreur.Message);
        _leaveRepo.Verify(r => r.AddAsync(It.IsAny<Leave>()), Times.Never);
    }

    // ---------------------------------------------------------------
    // 4. Chevauchement avec une autre demande
    // ---------------------------------------------------------------
    [Fact]
    public async Task AddAsync_ChevauchementAvecAutreDemande_Refuse()
    {
        // Arrange : solde suffisant, mais un congé existe déjà sur la période
        var lundi = ProchainLundi();
        _employeeRepo.Setup(r => r.GetByIdAsync(1))
                     .ReturnsAsync(new Employee { Id = 1, SoldeConges = 20, Role = "Employe" });
        _leaveRepo.Setup(r => r.HasOverlapAsync(1, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                  .ReturnsAsync(true);
        var demande = new Leave { EmployeeId = 1, StartDate = lundi, EndDate = lundi.AddDays(2) };

        // Act + Assert
        var erreur = await Assert.ThrowsAsync<BusinessException>(() => _service.AddAsync(demande));
        Assert.Contains("existe déjà", erreur.Message);
        _leaveRepo.Verify(r => r.AddAsync(It.IsAny<Leave>()), Times.Never);
    }

    // ---------------------------------------------------------------
    // 5. Demande normale : en attente, solde inchangé
    // ---------------------------------------------------------------
    [Fact]
    public async Task AddAsync_EmployeNormal_StatutPendingEtSoldeInchange()
    {
        // Arrange
        var lundi = ProchainLundi();
        var employe = new Employee { Id = 1, SoldeConges = 20, Role = "Employe" };
        _employeeRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(employe);
        var demande = new Leave { EmployeeId = 1, StartDate = lundi, EndDate = lundi.AddDays(4) };

        // Act
        await _service.AddAsync(demande);

        // Assert
        Assert.Equal("Pending", demande.Status);
        Assert.Equal(20, employe.SoldeConges); // le solde n'est déduit qu'à l'approbation
        _leaveRepo.Verify(r => r.AddAsync(demande), Times.Once);
    }

    // ---------------------------------------------------------------
    // 6. Directeur : auto-approbation et déduction immédiate
    // ---------------------------------------------------------------
    [Fact]
    public async Task AddAsync_Directeur_AutoApprouveEtDeduitLeSolde()
    {
        // Arrange : 5 jours ouvrables demandés par un directeur qui en a 20
        var lundi = ProchainLundi();
        var directeur = new Employee { Id = 1, SoldeConges = 20, Role = "Directeur", UserId = "user-directeur" };
        _employeeRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(directeur);
        var demande = new Leave { EmployeeId = 1, StartDate = lundi, EndDate = lundi.AddDays(4) };

        // Act
        await _service.AddAsync(demande);

        // Assert
        Assert.Equal("Approved", demande.Status);
        Assert.Equal("user-directeur", demande.ApprovedById);
        Assert.Equal(15, directeur.SoldeConges); // 20 - 5
        _employeeRepo.Verify(r => r.UpdateAsync(directeur), Times.Once);
        _leaveRepo.Verify(r => r.AddAsync(demande), Times.Once);
    }

    // ---------------------------------------------------------------
    // 7. Refus sans motif interdit
    // ---------------------------------------------------------------
    [Fact]
    public async Task RejectAsync_SansMotif_Refuse()
    {
        // Arrange : le manager (Id 10) de l'employé refuse, mais sans motif
        var demande = new Leave
        {
            Id = 1,
            EmployeeId = 2,
            Employee = new Employee { Id = 2, ManagerId = 10, Role = "Employe" }
        };
        _leaveRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(demande);
        _employeeRepo.Setup(r => r.GetByIdAsync(10))
                     .ReturnsAsync(new Employee { Id = 10, Role = "Manager" });

        // Act + Assert
        var erreur = await Assert.ThrowsAsync<BusinessException>(() => _service.RejectAsync(1, 10, ""));
        Assert.Contains("motif", erreur.Message);
        _leaveRepo.Verify(r => r.UpdateAsync(It.IsAny<Leave>()), Times.Never);
    }

    // ---------------------------------------------------------------
    // 8. Un manager ne peut pas approuver hors de son équipe
    // ---------------------------------------------------------------
    [Fact]
    public async Task ApproveAsync_ManagerDUneAutreEquipe_Refuse()
    {
        // Arrange : l'employé dépend du manager 10, mais c'est le manager 99 qui approuve
        var demande = new Leave
        {
            Id = 1,
            EmployeeId = 2,
            Employee = new Employee { Id = 2, ManagerId = 10, Role = "Employe" }
        };
        _leaveRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(demande);
        _employeeRepo.Setup(r => r.GetByIdAsync(99))
                     .ReturnsAsync(new Employee { Id = 99, Role = "Manager" });

        // Act + Assert
        var erreur = await Assert.ThrowsAsync<BusinessException>(() => _service.ApproveAsync(1, 99));
        Assert.Contains("droits", erreur.Message);
        _leaveRepo.Verify(r => r.UpdateAsync(It.IsAny<Leave>()), Times.Never);
    }
}
