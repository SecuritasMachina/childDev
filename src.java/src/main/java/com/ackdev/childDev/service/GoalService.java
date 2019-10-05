package com.ackdev.childDev.service;

import java.io.IOException;
import java.util.Collection;

import javax.servlet.http.HttpServletRequest;
import javax.ws.rs.QueryParam;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.web.bind.annotation.CrossOrigin;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.ackdev.childDev.bean.GoalBean;
import com.ackdev.childDev.jdo.GoalItem;
import com.ackdev.common.dto.CommonResponse;

import io.swagger.annotations.ApiOperation;
import io.swagger.annotations.ApiParam;
import io.swagger.annotations.ApiResponse;
import io.swagger.annotations.ApiResponses;
import io.swagger.annotations.Authorization;
@SpringBootApplication
@RestController
@CrossOrigin(origins = "*")
@RequestMapping("/rest/secure/goal")
public class GoalService {
	
	@Autowired 
	private GoalBean jb;

	
	@DeleteMapping("/{guid}")
	@ApiOperation(value = "Soft delete a journal entry", notes = "Expiration date is set to time of deletion", response = CommonResponse.Status.class, authorizations = @Authorization(value = "api_key"))
	@ApiResponses(value = { @ApiResponse(code = 404, message = "Invalid ID supplied"),
			@ApiResponse(code = 200, message = "success"), @ApiResponse(code = 401, message = "unauthorized") })
	public CommonResponse.Status delete(@ApiParam(value = "ID of property to return") @PathVariable String guid,
			@QueryParam("api_key") String pApiKey) throws IOException {
		// UserSession sUserSession = ThreadInstance.getUserSession();

		return null;
	}

	@DeleteMapping("/hardDelete/{guid}")
	@ApiOperation(value = "Hard delete a journal entry", notes = "Expiration date is set to time of deletion", response = CommonResponse.Status.class, authorizations = @Authorization(value = "api_key"))
	@ApiResponses(value = { @ApiResponse(code = 404, message = "Invalid ID supplied"),
			@ApiResponse(code = 200, message = "success"), @ApiResponse(code = 401, message = "unauthorized") })
	public CommonResponse.Status hardDelete(@ApiParam(value = "ID of property to return") @PathVariable String guid){
		return jb.delete(guid);
	}

	@GetMapping("/range/{startTS}/{endTS}")
	public Collection<GoalItem> getRange(@PathVariable Long startTS, @PathVariable Long endTS, HttpServletRequest request) {
		return jb.getRange(startTS, endTS);
	}
	@GetMapping("/{guid}")
	public GoalItem get(@PathVariable String guid) {
		return jb.get(guid);
	}

	@PostMapping( consumes = "application/json", produces = "application/json")
	public CommonResponse.Status saveJournalEntry(@RequestBody GoalItem journal) {
		return jb.update(journal);
	}

	@PostMapping(path = "/batch", consumes = "application/json", produces = "application/json")
	public Collection<CommonResponse.Status> saveBatchJournalEntry(@RequestBody Collection<GoalItem> journalCollection,
			HttpServletRequest request) {
		return jb.update(journalCollection);
	}
}
