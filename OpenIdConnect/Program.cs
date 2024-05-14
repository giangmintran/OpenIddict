using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenIdConnect;

public class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.

        builder.Services.AddControllers();
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        

        builder.Services.AddDbContext<ApplicationDbContext>(options =>
        {
            // Configure Entity Framework Core to use Microsoft SQL Server.
            options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));

            // Register the entity sets needed by OpenIddict.
            // Note: use the generic overload if you need to replace the default OpenIddict entities.
            options.UseOpenIddict();
        });

        builder
            .Services.AddOpenIddict()
            // Register the OpenIddict server components.
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore().UseDbContext<ApplicationDbContext>();
            })
            .AddServer(options =>
            {
                // Enable the token endpoint.
                options.SetTokenEndpointUris("/connect/token");

                // Enable the client credentials flow.
                options.AllowPasswordFlow().AllowRefreshTokenFlow();
                options.SetAccessTokenLifetime(TimeSpan.FromHours(2));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(3));

                // Accept anonymous clients (i.e clients that don't send a client_id).
                // options.AcceptAnonymousClients();

                // Disable mã hóa token (Vd: dùng dòng này thì jwt.io đọc được, cmt vào thì ko đọc đc)
                options.DisableAccessTokenEncryption();
                var key = new SymmetricSecurityKey(
                        Convert.FromBase64String("DRjd/GnduI3Efzen9V9BvbNUfc/VKgXltV7Kbk9sMkY=")
                    );
                // Register the signing and encryption credentials.
                options.AddEncryptionKey(key);
                options.AddSigningKey(key);
                // Register the ASP.NET Core host and configure the ASP.NET Core options.
                options
                    .UseAspNetCore()
                    .EnableTokenEndpointPassthrough()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableLogoutEndpointPassthrough()
                    .DisableTransportSecurityRequirement(); //không bắt https
                options.AddDevelopmentSigningCertificate();
            })
            // Register the OpenIddict validation components.
            .AddValidation(options =>
            {
                options.EnableTokenEntryValidation();
                options.EnableAuthorizationEntryValidation();
                // Import the configuration from the local OpenIddict server instance.
                options.UseLocalServer();
                var key = new SymmetricSecurityKey(
                        Convert.FromBase64String("DRjd/GnduI3Efzen9V9BvbNUfc/VKgXltV7Kbk9sMkY=")
                    );
                options.Configure(o => o.TokenValidationParameters.IssuerSigningKey = key);
                // Register the ASP.NET Core host.
                options.UseAspNetCore();
            });

        // Register the worker responsible of seeding the database with the sample clients.
        // Note: in a real world application, this step should be part of a setup script.
        builder.Services.AddHostedService<Worker>();
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var key = new SymmetricSecurityKey(
                        Convert.FromBase64String("DRjd/GnduI3Efzen9V9BvbNUfc/VKgXltV7Kbk9sMkY=")
                    );
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key
            };
            options.RequireHttpsMetadata = false;

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    //lấy token trong header
                    var accessToken = context.Request.Query.FirstOrDefault(q => q.Key == "access_token").Value.ToString();
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        accessToken = context.Request.Headers.FirstOrDefault(h => h.Key == "access_token").Value.ToString();
                    }

                    // If the request is for our hub...
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub"))
                    {
                        // Read the token out of the query string
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        }); ;
        builder.Services.AddSwaggerGen(option =>
        {
            option.SwaggerDoc(
                "v1",
                new OpenApiInfo
                {
                    Title = Assembly.GetEntryAssembly()?.GetName().Name,
                    Version = "v1"
                }
            );

            option.AddSecurityDefinition(
                JwtBearerDefaults.AuthenticationScheme,
                new OpenApiSecurityScheme
                {
                    Description =
                        "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    Name = "Authorization",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey
                }
            );

            option.AddSecurityRequirement(
                new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = JwtBearerDefaults.AuthenticationScheme
                            }
                        },
                        Array.Empty<string>()
                    }
                }
            );

            // Set the comments path for the Swagger JSON and UI.**
            var xmlFile = Path.Combine(
                AppContext.BaseDirectory,
                $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"
            );
            if (File.Exists(xmlFile))
            {
                option.IncludeXmlComments(xmlFile);
            }
            var projectDependencies = Assembly
                .GetEntryAssembly()!
                .CustomAttributes.SelectMany(c =>
                    c.ConstructorArguments.Select(ca => ca.Value?.ToString())
                )
                .Where(o => o != null)
                .ToList();
            foreach (var assembly in projectDependencies)
            {
                var otherXml = Path.Combine(AppContext.BaseDirectory, $"{assembly}.xml");
                if (File.Exists(otherXml))
                {
                    option.IncludeXmlComments(otherXml);
                }
            }
            option.CustomSchemaIds(x => x.FullName);
        });

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseDeveloperExceptionPage();

        app.UseRouting();
        app.UseCors();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.Run();
    }
}
