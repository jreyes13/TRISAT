//
//  LocationViewController.m
// SDK Sample
//
//  Created by Sharad Garg on 11/12/14.
//  Copyright (c) 2014 Aman Enterprises Inc. All rights reserved.
//

#import "LocationViewController.h"
#import "GPSManager.h"


@interface LocationViewController () <CLLocationManagerDelegate>

@end

@implementation LocationViewController

- (id)initWithNibName:(NSString *)nibNameOrNil bundle:(NSBundle *)nibBundleOrNil
{
    self = [super initWithNibName:nibNameOrNil bundle:nibBundleOrNil];
    if (self) {
        // Custom initialization
    }
    return self;
}

- (void)viewDidLoad
{
    [super viewDidLoad];
    CLLocationManager *locationManager = [GPSManager sharedLocationManager];
	
	locationManager.delegate = self;
	[locationManager startUpdatingLocation];
    [locationManager startUpdatingHeading];
}

- (void)didReceiveMemoryWarning
{
    [super didReceiveMemoryWarning];
    // Dispose of any resources that can be recreated.
}

#pragma mark - update
- (void)updateText
{
    CLLocation *loc = [GPSManager sharedLocationManager].location;
    
    // Core location
    self.clLatitude.text = [NSString stringWithFormat:@"%.06f", loc.coordinate.latitude];
    self.clLongitude.text = [NSString stringWithFormat:@"%.06f", loc.coordinate.longitude];
    self.clAltitude.text = [NSString stringWithFormat:@"%.02f", loc.altitude];
//    self.clHeading.text = [NSString stringWithFormat:@"%.02f", heading.trueHeading];
    self.clCourse.text = [NSString stringWithFormat:@"%.02f", loc.course];
    self.clSpeed.text = [NSString stringWithFormat:@"%.02f", loc.speed];
    
}

- (void)locationManager:(CLLocationManager *)manager
    didUpdateToLocation:(CLLocation *)newLocation
           fromLocation:(CLLocation *)oldLocation
{
    MKCoordinateSpan span = MKCoordinateSpanMake(0.005,0.005);
    MKCoordinateRegion region = MKCoordinateRegionMake(newLocation.coordinate, span);
    
    [self.mapView setRegion:region animated:YES];
    self.mapView.showsUserLocation = YES;
    [self updateText];
}

- (void)locationManager:(CLLocationManager *)manager didUpdateHeading:(CLHeading *)newHeading
{
    [self updateText];
}

- (void)updateGPSDataWith:(nmeaINFO)info {
    // altitude
    self.gpsAltitude.text = [NSString stringWithFormat:@"%0.2f", info.elv];
    // latitude
    double latdeg = nmea_ndeg2degree(info.lat);
    self.gpsLatitude.text = [NSString stringWithFormat:@"%0.8f", latdeg];
    // long
    double londeg = nmea_ndeg2degree(info.lon);
    self.gpsLongitude.text = [NSString stringWithFormat:@"%0.8f", londeg];
    // gst precision
    if (info.smask & GPGST) {
        self.gpsGSTPrecisionLabel.text = [NSString stringWithFormat:@"%0.2f m", sqrt(pow(info.dev_lat, 2) + pow(info.dev_lon, 2))];
    }
    else {
        self.gpsGSTPrecisionLabel.text = @"N/A";
    }
    
    self.gpsHDOP.text = [NSString stringWithFormat:@"%0.2f", info.HDOP];
    self.gpsPDOP.text = [NSString stringWithFormat:@"%0.2f", info.PDOP];
    self.gpsVDOP.text = [NSString stringWithFormat:@"%0.2f", info.VDOP];
    
    self.gpsNumSatUsed.text = [NSString stringWithFormat:@"%2i / %2i", info.GPSsatinfo.inuse, info.GLONASSsatinfo.inuse];
    self.gpsNumSatTracked.text = [NSString stringWithFormat:@"%2i / %2i", info.GPSsatinfo.inview, info.GLONASSsatinfo.inview];
    self.gpsAgeDiffCorrect.text = [NSString stringWithFormat:@"%0.1f", info.dgps_age];
    
    
    switch (info.sig) {
        case 0:
            self.gpsFixType.text = @"None";
            break;
        case 1:
            self.gpsFixType.text = @"GPS";
            break;
        case 2:
            self.gpsFixType.text = @"DGPS";
            break;
        case 3:
            self.gpsFixType.text = @"GPS PPP";
            break;
        case 4:
            self.gpsFixType.text = @"Fixed";
            break;
        case 5:
            self.gpsFixType.text = @"Float";
            break;
            
        default:
            break;
            
        
    }
}

/*
#pragma mark - Navigation

// In a storyboard-based application, you will often want to do a little preparation before navigation
- (void)prepareForSegue:(UIStoryboardSegue *)segue sender:(id)sender
{
    // Get the new view controller using [segue destinationViewController].
    // Pass the selected object to the new view controller.
}
*/

@end
